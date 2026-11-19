using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Configuration;
using HealthOS.Core.Identity;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Modules;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.CoreApi.Composition;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>
/// M3 gate: five distinct customer profiles are instantiated from templates without code edits, and each
/// loads exactly the expected modules, forms, workflows, roles and profile-driven settings.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class OnboardingTests(PostgresFixture db) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ApiHost(db);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    public sealed record Expected(string Template, string Country, string[] Modules, string[] Forms, string[] Workflows, string[] Roles, bool FourEyes, string Language);

    public static TheoryData<string> Templates => ["general-clinic", "dental-clinic", "eye-clinic", "pharma-qa", "equipment-service"];

    private static readonly Dictionary<string, Expected> Profiles = new()
    {
        ["general-clinic"] = new("general-clinic", "IN", ["clinicos.core"], ["general-intake"], ["appointment"], ["doctor", "front-desk"], false, "en"),
        ["dental-clinic"] = new("dental-clinic", "IN", ["clinicos.core", "specialty.dental"], ["dental-finding"], ["treatment-plan"], ["dentist", "dental-assistant", "front-desk"], false, "en"),
        ["eye-clinic"] = new("eye-clinic", "EU-FR", ["clinicos.core", "specialty.vision"], ["eye-exam"], ["procedure-follow-up"], ["ophthalmologist", "optometrist", "front-desk"], false, "fr"),
        ["pharma-qa"] = new("pharma-qa", "EU-DE", ["careops.quality"], ["deviation-report"], ["deviation-capa", "sop-approval"], ["qa-manager", "document-author"], true, "de"),
        ["equipment-service"] = new("equipment-service", "IN", ["medtechos.assets"], ["service-report"], ["service-job"], ["service-manager", "field-engineer"], false, "en"),
    };

    private async Task<(Guid Id, string Email)> NewUserAsync(string label)
    {
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        var email = $"{label}-{Guid.NewGuid():N}@example.invalid";
        var id = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(new ExternalIdentity("https://idp.test", label + "-" + Guid.NewGuid(), "Synthetic " + label, email), Ct);
        return (id, email);
    }

    private async Task<Guid> NewOperatorAsync()
    {
        var (op, email) = await NewUserAsync("operator");
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
        await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(email, Ct);
        return op;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Template_instantiates_the_expected_tenant_without_code_changes(string template)
    {
        PostgresFixture.SkipIfUnavailable();
        var expected = Profiles[template];
        var op = await NewOperatorAsync();
        var tenant = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest("o" + Guid.NewGuid().ToString("N")[..10], "Synthetic " + template), Ct)).Read<TenantSummary>();

        var preview = await (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/preview",
            new OnboardingRequest(template, expected.Country, 30), Ct)).Read<OnboardingResult>();
        Assert.True(preview.Succeeded, string.Join("; ", preview.Problems));
        Assert.All(preview.Steps, s => Assert.Equal("planned", s.Status));

        var applied = await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/apply", new OnboardingRequest(template, expected.Country, 30), Ct);
        var result = await applied.Content.ReadFromJsonAsync<OnboardingResult>(Ct);
        Assert.True(applied.IsSuccessStatusCode, string.Join("; ", result!.Problems));
        Assert.All(result!.Steps, s => Assert.Equal("done", s.Status));

        var (admin, adminEmail) = await NewUserAsync("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/admins", new { email = adminEmail }, Ct)).StatusCode);
        var client = _api.As(admin, tenant.Id);

        var modules = await (await client.GetAsync("/api/core/modules", Ct)).Read<List<ModuleStatusDto>>();
        Assert.Equal(expected.Modules.Order(), modules.Where(m => m.Kind != "platform" && m.State == "Active").Select(m => m.Id).Order());

        var forms = await (await client.GetAsync("/api/core/configuration/forms", Ct)).Read<List<DefinitionSummary>>();
        Assert.Equal(expected.Forms.Order(), forms.Select(f => f.Code).Order());
        Assert.All(forms, f => Assert.Equal("template", f.Source));
        var workflows = await (await client.GetAsync("/api/core/configuration/workflows", Ct)).Read<List<DefinitionSummary>>();
        Assert.Equal(expected.Workflows.Order(), workflows.Select(w => w.Code).Order());

        var roles = await (await client.GetAsync("/api/core/identity/roles", Ct)).Read<List<RoleDto>>();
        Assert.Equal(expected.Roles.Append("tenant-admin").Order(), roles.Select(r => r.Code).Order());

        var nav = await (await client.GetAsync("/api/core/navigation", Ct)).Read<List<NavigationItem>>();
        Assert.Equal(expected.Modules.Order(), nav.Select(n => n.ModuleId).Distinct().Order());

        var effective = (await (await client.GetAsync("/api/core/configuration/effective", Ct)).Content.ReadFromJsonAsync<List<ResolvedSetting>>(ConfigurationService.Json, Ct))!;
        Assert.Equal(expected.FourEyes, effective.Single(s => s.Key == "core.configuration.approval.fourEyes").Value.GetBoolean());
        Assert.Equal(expected.Language, effective.Single(s => s.Key == "core.tenancy.locale.language").Value.GetString());

        // Idempotent: applying again changes nothing (no new definition versions).
        Assert.Equal(HttpStatusCode.OK, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/apply", new OnboardingRequest(template, expected.Country, 30), Ct)).StatusCode);
        Assert.All(await (await client.GetAsync("/api/core/configuration/forms", Ct)).Read<List<DefinitionSummary>>(), f => Assert.Equal(1, f.Version));
    }

    [Fact]
    public async Task Preview_reports_problems_without_changing_anything()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var tenant = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest("p" + Guid.NewGuid().ToString("N")[..10], "Synthetic preview"), Ct)).Read<TenantSummary>();

        var unknown = await (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/preview", new OnboardingRequest("no-such-template", "IN", null), Ct)).Read<OnboardingResult>();
        Assert.False(unknown.Succeeded);
        var badCountry = await (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/preview", new OnboardingRequest("general-clinic", "XX", null), Ct)).Read<OnboardingResult>();
        Assert.Contains(badCountry.Problems, p => p.Contains("country", StringComparison.OrdinalIgnoreCase));

        var refused = await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/onboarding/apply", new OnboardingRequest("general-clinic", "XX", null), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        // Nothing was applied: no licences were granted.
        Assert.Empty(await (await _api.As(op).GetAsync($"/api/platform/tenants/{tenant.Id}/entitlements", Ct)).Read<List<object>>());
    }

    [Fact]
    public void Every_template_is_valid_against_the_full_package()
    {
        var templates = new TemplateCatalog();
        Assert.Equal(5, templates.All.Count);
        var installed = InstalledModules.Create().Select(m => m.Manifest.Id).ToHashSet();
        Assert.All(templates.All.SelectMany(t => t.Modules), m => Assert.Contains(m, installed));
    }
}
