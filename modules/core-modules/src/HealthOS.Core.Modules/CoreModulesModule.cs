using HealthOS.Core.Licensing.Contracts;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Core.Modules;

public sealed class TenantModule
{
    public Guid TenantId { get; set; }
    public required string ModuleId { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

public sealed class ModulesDbContext(DbContextOptions<ModulesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_modules";

    protected override string Schema => SchemaName;

    public DbSet<TenantModule> TenantModules => Set<TenantModule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TenantModule>(e =>
        {
            e.ToTable("tenant_module");
            e.HasKey(x => new { x.TenantId, x.ModuleId });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.ModuleId).HasColumnName("module_id");
            e.Property(x => x.Enabled).HasColumnName("enabled");
            e.Property(x => x.ChangedAt).HasColumnName("changed_at");
            e.Property(x => x.ChangedBy).HasColumnName("changed_by");
        });
    }
}

/// <summary>Request-scoped activation for the active tenant (computed once per request).</summary>
internal sealed class ModuleActivationService(
    ModulesDbContext db, IEntitlements entitlements, IRequestContext context, IReadOnlyList<IHealthOsModule> modules) : IModuleActivation
{
    private IReadOnlyList<ModuleActivation>? _cache;

    public async Task<IReadOnlyList<ModuleActivation>> GetAllAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var package = modules.Select(m => m.Manifest).ToList();
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var licences = new Dictionary<string, LicenseState>(StringComparer.Ordinal);

        if (context.TenantId is { } tenantId)
        {
            enabled.UnionWith(await db.TenantModules.AsNoTracking().Where(t => t.TenantId == tenantId && t.Enabled)
                .Select(t => t.ModuleId).ToListAsync(cancellationToken).ConfigureAwait(false));
            foreach (var l in package.Where(m => m.License is not null).Select(m => m.License!).DistinctBy(l => LicenseFeatures.Key(l.ProductCode, l.Feature)))
            {
                var key = LicenseFeatures.Key(l.ProductCode, l.Feature);
                licences[key] = (await entitlements.GetAsync(key, cancellationToken).ConfigureAwait(false)).State switch
                {
                    EntitlementState.Active => LicenseState.Active,
                    EntitlementState.ReadOnly => LicenseState.ReadOnly,
                    _ => LicenseState.None,
                };
            }
        }

        return _cache = TenantActivation.Resolve(package, enabled, l => licences.GetValueOrDefault(LicenseFeatures.Key(l.ProductCode, l.Feature), LicenseState.None));
    }

    public async Task<ModuleActivation?> GetAsync(string moduleId, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(a => a.Id == moduleId);

    public async Task<IReadOnlySet<string>> EnabledAsync(CancellationToken ct) =>
        (await GetAllAsync(ct).ConfigureAwait(false)).Where(a => a.Enabled && a.Manifest.Kind != ModuleKind.Platform).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

    public async Task<LicenseState> LicenseAsync(ModuleLicense l, CancellationToken ct) =>
        (await entitlements.GetAsync(LicenseFeatures.Key(l.ProductCode, l.Feature), ct).ConfigureAwait(false)).State switch
        {
            EntitlementState.Active => LicenseState.Active,
            EntitlementState.ReadOnly => LicenseState.ReadOnly,
            _ => LicenseState.None,
        };
}

public sealed record ModuleStatusDto(string Id, string DisplayName, string Kind, string Version, bool Enabled, string State, IReadOnlyList<ActivationReason> Reasons, string? LicenseFeature);

public sealed record EnableModuleRequest(bool IncludeDependencies);

public sealed record ActivationProblem(string Title, IReadOnlyList<ActivationReason> Reasons, IReadOnlyList<string> Required);

public sealed class CoreModulesModule : HealthOsModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<ModulesDbContext>(Manifest.Id, ModulesDbContext.SchemaName);
        services.AddScoped<ModuleActivationService>();
        services.AddScoped<IModuleActivation>(sp => sp.GetRequiredService<ModuleActivationService>());
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/api/core/modules").WithTags("core.modules");
        tenant.MapGet("/", async (IModuleActivation activation, CancellationToken ct) =>
                TypedResults.Ok((await activation.GetAllAsync(ct)).Select(ToDto)))
            .RequirePermission("core.modules.status.read");
        tenant.MapPost("/{moduleId}/enable", (string moduleId, EnableModuleRequest? req, IRequestContext ctx, IServiceProvider sp, CancellationToken ct) =>
                SetAsync(sp, ctx.TenantId!.Value, moduleId, enable: true, req?.IncludeDependencies ?? false, ct))
            .RequirePermission("core.modules.activation.manage");
        tenant.MapPost("/{moduleId}/disable", (string moduleId, IRequestContext ctx, IServiceProvider sp, CancellationToken ct) =>
                SetAsync(sp, ctx.TenantId!.Value, moduleId, enable: false, false, ct))
            .RequirePermission("core.modules.activation.manage");

        // Onboarding by platform operators: same rules, evaluated in the target tenant's context.
        var platform = endpoints.MapGroup("/api/platform/tenants/{tenantId:guid}/modules").WithTags("platform.modules");
        platform.MapGet("/", async (Guid tenantId, RequestContext ctx, IServiceProvider sp, CancellationToken ct) =>
                await ctx.RunAsTenantAsync(tenantId, async () =>
                    (IResult)TypedResults.Ok((await Fresh(sp).GetAllAsync(ct)).Select(ToDto))))
            .RequirePermission("core.modules.tenant-activation.manage");
        platform.MapPost("/{moduleId}/enable", (Guid tenantId, string moduleId, EnableModuleRequest? req, IServiceProvider sp, CancellationToken ct) =>
                SetAsync(sp, tenantId, moduleId, enable: true, req?.IncludeDependencies ?? false, ct))
            .RequirePermission("core.modules.tenant-activation.manage");
        platform.MapPost("/{moduleId}/disable", (Guid tenantId, string moduleId, IServiceProvider sp, CancellationToken ct) =>
                SetAsync(sp, tenantId, moduleId, enable: false, false, ct))
            .RequirePermission("core.modules.tenant-activation.manage");
    }

    /// <summary>A new activation service instance (no per-request cache), for evaluating another tenant.</summary>
    private static ModuleActivationService Fresh(IServiceProvider sp) => ActivatorUtilities.CreateInstance<ModuleActivationService>(sp);

    private static async Task<IResult> SetAsync(IServiceProvider sp, Guid tenantId, string moduleId, bool enable, bool includeDependencies, CancellationToken ct)
    {
        var ctx = sp.GetRequiredService<RequestContext>();
        var db = sp.GetRequiredService<ModulesDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var package = sp.GetRequiredService<IReadOnlyList<IHealthOsModule>>().Select(m => m.Manifest).ToList();

        // Evaluate and write in the target tenant's context (identical to tenant scope when called by the tenant).
        return await ctx.RunAsTenantAsync(tenantId, async () =>
        {
            var activation = Fresh(sp);
            var enabled = await activation.EnabledAsync(ct);
            var licences = new Dictionary<ModuleLicense, LicenseState>();
            foreach (var l in package.Where(m => m.License is not null).Select(m => m.License!).Distinct())
            {
                licences[l] = await activation.LicenseAsync(l, ct);
            }

            List<string> changes;
            string action;
            if (enable)
            {
                var plan = TenantActivation.PlanEnable(package, enabled, l => licences.GetValueOrDefault(l, LicenseState.None), moduleId);
                if (!plan.Allowed)
                {
                    return Results.BadRequest(new ActivationProblem("The module cannot be enabled.", plan.Problems, plan.ModulesToEnable));
                }

                if (plan.ModulesToEnable.Count > 1 && !includeDependencies)
                {
                    return Results.Conflict(new ActivationProblem("Other modules must be enabled first.", [], plan.ModulesToEnable));
                }

                changes = plan.ModulesToEnable.ToList();
                action = "core.modules.module.enabled";
            }
            else
            {
                if (package.FirstOrDefault(m => m.Id == moduleId) is not { } target || target.Kind == ModuleKind.Platform)
                {
                    return Results.BadRequest(new ActivationProblem("Only installed, non-platform modules can be disabled.", [], []));
                }

                var dependents = TenantActivation.EnabledDependents(package, enabled, moduleId);
                if (dependents.Count > 0)
                {
                    return Results.Conflict(new ActivationProblem("Disable the modules that depend on this one first.", [], dependents));
                }

                changes = [moduleId];
                action = "core.modules.module.disabled";
            }

            var now = clock.GetUtcNow();
            foreach (var id in changes)
            {
                var row = await db.TenantModules.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.ModuleId == id, ct);
                if (row is null)
                {
                    db.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleId = id, Enabled = enable, ChangedAt = now, ChangedBy = ctx.UserId });
                }
                else
                {
                    row.Enabled = enable;
                    row.ChangedAt = now;
                    row.ChangedBy = ctx.UserId;
                }
            }

            // Audited in the tenant's chain; module data is never deleted by disabling.
            await db.SaveChangesWithAuditAsync(new AuditRecord(action, "module", moduleId,
                Details: new Dictionary<string, object?> { ["modules"] = changes.ToArray(), ["dataRetained"] = true }), ct);

            return (IResult)TypedResults.Ok((await Fresh(sp).GetAllAsync(ct)).Select(ToDto));
        });
    }

    private static ModuleStatusDto ToDto(ModuleActivation a) => new(
        a.Id, a.Manifest.DisplayName, a.Manifest.Kind.ToString().ToLowerInvariant(), a.Manifest.Version, a.Enabled, a.State.ToString(), a.Reasons,
        a.Manifest.License is { } l ? LicenseFeatures.Key(l.ProductCode, l.Feature) : null);
}
