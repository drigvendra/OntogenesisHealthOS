using System.Reflection;
using System.Text.Json;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Configuration;

/// <summary>A customer profile template (Templates/*.json): profiles, modules, roles, forms, workflows.</summary>
public sealed record OnboardingTemplate(
    string Code, string DisplayName, string Description, string Industry, string? Specialty,
    IReadOnlyList<string> Modules, IReadOnlyList<RoleTemplate> Roles, IReadOnlyList<JsonElement> Forms, IReadOnlyList<JsonElement> Workflows);

public sealed class TemplateCatalog
{
    public TemplateCatalog()
    {
        var asm = Assembly.GetExecutingAssembly();
        All = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Templates.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n =>
            {
                using var s = asm.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<OnboardingTemplate>(s, ConfigurationService.Json)!;
            })
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<OnboardingTemplate> All { get; }

    public OnboardingTemplate? Find(string code) => All.FirstOrDefault(t => t.Code == code);
}

public sealed record OnboardingRequest(string Template, string Country, int? TrialDays);

public sealed record OnboardingStep(string Kind, string Item, string Status, string? Detail = null);

public sealed record OnboardingResult(string Template, bool Succeeded, IReadOnlyList<OnboardingStep> Steps, IReadOnlyList<string> Problems);

/// <summary>
/// Admin onboarding (spec §8): country → industry → specialty → modules (with trial licences) → roles →
/// forms/workflows. Preview never changes anything; apply is idempotent (re-applying is safe). Every
/// step is performed and audited by the module that owns the data.
/// </summary>
public sealed class OnboardingService(
    TemplateCatalog templates,
    ProfileCatalog profiles,
    IReadOnlyList<IHealthOsModule> installed,
    PermissionCatalog permissions,
    DefinitionStore definitions,
    IConfigurationAdministration configuration,
    ILicenseAdministration licences,
    IModuleAdministration moduleAdmin,
    IRoleAdministration roles,
    TimeProvider clock)
{
    public OnboardingResult Preview(OnboardingRequest req)
    {
        var problems = new List<string>();
        var steps = new List<OnboardingStep>();
        if (templates.Find(req.Template) is not { } t)
        {
            return new OnboardingResult(req.Template, false, [], [$"Unknown template '{req.Template}'."]);
        }

        if (profiles.Find(ConfigScope.Country, req.Country) is null)
        {
            problems.Add($"Unknown country profile '{req.Country}'.");
        }

        steps.Add(new("profile", $"{req.Country} / {t.Industry}{(t.Specialty is null ? "" : " / " + t.Specialty)}", "planned"));

        var byId = installed.ToDictionary(m => m.Manifest.Id, m => m.Manifest, StringComparer.Ordinal);
        foreach (var moduleId in t.Modules)
        {
            if (!byId.TryGetValue(moduleId, out var m))
            {
                problems.Add($"Module '{moduleId}' is not installed in this deployment (bundle).");
                continue;
            }

            foreach (var l in Closure(m, byId).Where(x => x.License is not null).Select(x => x.License!).Distinct())
            {
                steps.Add(new("licence", $"{l.ProductCode}:{l.Feature}", "planned", "trial unless already licensed"));
            }

            steps.Add(new("module", moduleId, "planned"));
        }

        foreach (var r in t.Roles)
        {
            var bad = r.Permissions.Where(p => permissions.Find(p) is not { Scope: PermissionScope.Tenant }).ToList();
            problems.AddRange(bad.Select(p => $"Role '{r.Code}': permission '{p}' is not available in this deployment."));
            steps.Add(new("role", r.Code, "planned", $"{r.Permissions.Count} permissions"));
        }

        foreach (var (kind, docs) in new[] { (DefinitionStore.Form, t.Forms), (DefinitionStore.Workflow, t.Workflows) })
        {
            foreach (var d in docs)
            {
                var code = d.TryGetProperty("code", out var c) ? c.GetString() ?? "?" : "?";
                problems.AddRange(definitions.Validate(kind, d).Select(p => $"{kind} '{code}': {p}"));
                steps.Add(new(kind, code, "planned"));
            }
        }

        return new OnboardingResult(t.Code, problems.Count == 0, steps.DistinctBy(s => (s.Kind, s.Item)).ToList(), problems);
    }

    public async Task<OnboardingResult> ApplyAsync(Guid tenantId, OnboardingRequest req, CancellationToken ct)
    {
        var preview = Preview(req);
        if (!preview.Succeeded)
        {
            return preview;
        }

        var t = templates.Find(req.Template)!;
        var steps = new List<OnboardingStep>();
        var problems = new List<string>();
        var trialEnds = clock.GetUtcNow().AddDays(Math.Clamp(req.TrialDays ?? 30, 1, 365));

        var profileProblems = await configuration.SetProfileAsync(tenantId, req.Country, t.Industry, t.Specialty, ct).ConfigureAwait(false);
        steps.Add(new("profile", $"{req.Country} / {t.Industry}", profileProblems.Count == 0 ? "done" : "failed", string.Join("; ", profileProblems)));
        problems.AddRange(profileProblems);

        // All licences first: activation checks within this request must see every grant.
        var byId = installed.ToDictionary(m => m.Manifest.Id, m => m.Manifest, StringComparer.Ordinal);
        foreach (var l in t.Modules.SelectMany(id => Closure(byId[id], byId)).Where(x => x.License is not null).Select(x => x.License!).Distinct())
        {
            await licences.GrantIfMissingAsync(tenantId, $"{l.ProductCode}:{l.Feature}", trialEnds, "trial", ct).ConfigureAwait(false);
            steps.Add(new("licence", $"{l.ProductCode}:{l.Feature}", "done"));
        }

        foreach (var moduleId in t.Modules)
        {
            var enabled = await moduleAdmin.EnableAsync(tenantId, moduleId, includeDependencies: true, ct).ConfigureAwait(false);
            steps.Add(new("module", moduleId, enabled.Succeeded ? "done" : "failed", string.Join("; ", enabled.Problems)));
            problems.AddRange(enabled.Problems);
        }

        foreach (var r in t.Roles)
        {
            var roleProblems = await roles.EnsureRoleAsync(tenantId, r, ct).ConfigureAwait(false);
            steps.Add(new("role", r.Code, roleProblems.Count == 0 ? "done" : "failed", string.Join("; ", roleProblems)));
            problems.AddRange(roleProblems);
        }

        foreach (var (kind, docs) in new[] { (DefinitionStore.Form, t.Forms), (DefinitionStore.Workflow, t.Workflows) })
        {
            foreach (var d in docs)
            {
                var defProblems = await configuration.PublishDefinitionAsync(tenantId, kind, d, "template", ct).ConfigureAwait(false);
                steps.Add(new(kind, d.GetProperty("code").GetString()!, defProblems.Count == 0 ? "done" : "failed", string.Join("; ", defProblems)));
                problems.AddRange(defProblems);
            }
        }

        return new OnboardingResult(t.Code, problems.Count == 0, steps.DistinctBy(s => (s.Kind, s.Item)).ToList(), problems);
    }

    private static IEnumerable<ModuleManifest> Closure(ModuleManifest m, Dictionary<string, ModuleManifest> byId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<ModuleManifest>([m]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur.Id))
            {
                continue;
            }

            yield return cur;
            foreach (var d in cur.Dependencies.Where(d => byId.ContainsKey(d.Id)))
            {
                stack.Push(byId[d.Id]);
            }
        }
    }
}

/// <summary>Configuration side of onboarding: tenant profile and template definitions.</summary>
internal sealed class ConfigurationAdministration(ConfigurationDbContext db, ProfileCatalog profiles, DefinitionStore store, IRequestContext ctx, TimeProvider clock)
    : IConfigurationAdministration
{
    public async Task<IReadOnlyList<string>> SetProfileAsync(Guid tenantId, string country, string industry, string? specialty, CancellationToken ct)
    {
        var problems = new List<string>();
        var c = profiles.Find(ConfigScope.Country, country);
        var i = profiles.Find(ConfigScope.Industry, industry);
        var s = specialty is null ? null : profiles.Find(ConfigScope.Specialty, specialty);
        if (c is null) problems.Add($"Unknown country profile '{country}'.");
        if (i is null) problems.Add($"Unknown industry profile '{industry}'.");
        if (specialty is not null && s is null) problems.Add($"Unknown specialty profile '{specialty}'.");
        if (problems.Count > 0)
        {
            return problems;
        }

        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            profile = new TenantProfile { TenantId = tenantId, Country = c!.Key, Industry = i!.Key };
            db.Profiles.Add(profile);
        }

        profile.Country = c!.Key;
        profile.Industry = i!.Key;
        profile.Specialty = s?.Key;
        profile.UpdatedAt = clock.GetUtcNow();
        profile.UpdatedBy = ctx.UserId;
        await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.profile.set", "tenant", tenantId.ToString(),
            Details: new Dictionary<string, object?> { ["country"] = profile.Country, ["industry"] = profile.Industry, ["specialty"] = profile.Specialty }), ct).ConfigureAwait(false);
        return [];
    }

    public async Task<IReadOnlyList<string>> PublishDefinitionAsync(Guid tenantId, string kind, JsonElement document, string source, CancellationToken ct) =>
        (await store.PublishAsync(tenantId, kind, document, source, ctx.UserId, ct, skipIfUnchanged: true).ConfigureAwait(false)).Problems;
}

internal static class OnboardingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var p = endpoints.MapGroup("/api/platform").WithTags("platform.onboarding");
        p.MapGet("/onboarding/templates", (TemplateCatalog templates) =>
                TypedResults.Ok(templates.All.Select(t => new { t.Code, t.DisplayName, t.Description, t.Industry, t.Specialty, t.Modules, roles = t.Roles.Select(r => r.Code), forms = t.Forms.Count, workflows = t.Workflows.Count })))
            .RequirePermission("core.configuration.onboarding.apply");
        p.MapPost("/tenants/{tenantId:guid}/onboarding/preview", (Guid tenantId, OnboardingRequest req, OnboardingService onboarding) =>
                TypedResults.Ok(onboarding.Preview(req)))
            .RequirePermission("core.configuration.onboarding.apply");
        p.MapPost("/tenants/{tenantId:guid}/onboarding/apply", async (Guid tenantId, OnboardingRequest req, OnboardingService onboarding, CancellationToken ct) =>
            {
                var result = await onboarding.ApplyAsync(tenantId, req, ct);
                return result.Succeeded ? Results.Ok(result) : Results.UnprocessableEntity(result);
            })
            .RequirePermission("core.configuration.onboarding.apply");
    }
}
