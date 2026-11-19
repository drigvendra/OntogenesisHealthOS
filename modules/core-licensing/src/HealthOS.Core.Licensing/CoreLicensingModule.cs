using System.Text.RegularExpressions;
using HealthOS.Core.Licensing.Contracts;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Core.Licensing;

public sealed class Entitlement
{
    public Guid TenantId { get; set; }
    public required string Feature { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public int GraceDays { get; set; } = 30;
    public string Source { get; set; } = "manual";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class LicensingDbContext(DbContextOptions<LicensingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_licensing";

    protected override string Schema => SchemaName;

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Entitlement>(e =>
        {
            e.ToTable("entitlement");
            e.HasKey(x => new { x.TenantId, x.Feature });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Feature).HasColumnName("feature");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.ValidTo).HasColumnName("valid_to");
            e.Property(x => x.GraceDays).HasColumnName("grace_days");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        });
    }
}

public static class EntitlementRules
{
    public static EntitlementStatus Evaluate(string feature, Entitlement? e, DateTimeOffset now)
    {
        if (e is null || now < e.ValidFrom)
        {
            return new EntitlementStatus(feature, EntitlementState.None, null, null);
        }

        if (e.ValidTo is null || now < e.ValidTo)
        {
            return new EntitlementStatus(feature, EntitlementState.Active, e.ValidTo, e.ValidTo?.AddDays(e.GraceDays));
        }

        // Expired: never deleted, never "None" again — data must stay readable for retention and export.
        return new EntitlementStatus(feature, EntitlementState.ReadOnly, e.ValidTo, e.ValidTo.Value.AddDays(e.GraceDays));
    }
}

/// <summary>Request-scoped, cached per feature. Row-level security restricts reads to the active tenant.</summary>
internal sealed class Entitlements(LicensingDbContext db, IRequestContext context, TimeProvider clock) : IEntitlements
{
    private readonly Dictionary<string, EntitlementStatus> _cache = new(StringComparer.Ordinal);

    public async Task<EntitlementStatus> GetAsync(string feature, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(feature, out var cached))
        {
            return cached;
        }

        if (context.TenantId is not { } tenantId)
        {
            return new EntitlementStatus(feature, EntitlementState.None, null, null);
        }

        var row = await db.Entitlements.AsNoTracking()
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Feature == feature, cancellationToken).ConfigureAwait(false);
        return _cache[feature] = EntitlementRules.Evaluate(feature, row, clock.GetUtcNow());
    }
}

public sealed record GrantEntitlementRequest(string Feature, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, int? GraceDays, string? Source);

public sealed record LicensableFeatureDto(string Feature, string ModuleId, string DisplayName);

public sealed record EntitlementDto(string Feature, string State, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo, int GraceDays, string Source);

public sealed partial class CoreLicensingModule : HealthOsModule
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,31}:[a-z][a-z0-9.-]{0,95}$")]
    private static partial Regex FeaturePattern();

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<LicensingDbContext>(Manifest.Id, LicensingDbContext.SchemaName);
        services.AddScoped<IEntitlements, Entitlements>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform").WithTags("platform.licensing");

        platform.MapGet("/licensing/features", (IReadOnlyList<IHealthOsModule> modules) =>
                TypedResults.Ok(modules.Where(m => m.Manifest.License is not null)
                    .Select(m => new LicensableFeatureDto(LicenseFeatures.Key(m.Manifest.License!.ProductCode, m.Manifest.License.Feature), m.Manifest.Id, m.Manifest.DisplayName))
                    .OrderBy(f => f.Feature)))
            .RequirePermission("core.licensing.features.read");

        platform.MapGet("/tenants/{tenantId:guid}/entitlements", async (Guid tenantId, LicensingDbContext db, TimeProvider clock, CancellationToken ct) =>
                TypedResults.Ok((await db.Entitlements.AsNoTracking().Where(e => e.TenantId == tenantId).OrderBy(e => e.Feature).ToListAsync(ct))
                    .Select(e => ToDto(e, clock.GetUtcNow()))))
            .RequirePermission("core.licensing.entitlements.manage");

        platform.MapPut("/tenants/{tenantId:guid}/entitlements", async (Guid tenantId, GrantEntitlementRequest req, LicensingDbContext db,
                IReadOnlyList<IHealthOsModule> modules, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var known = modules.Where(m => m.Manifest.License is not null)
                    .Select(m => LicenseFeatures.Key(m.Manifest.License!.ProductCode, m.Manifest.License.Feature)).ToHashSet(StringComparer.Ordinal);
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(req.Feature) || !FeaturePattern().IsMatch(req.Feature) || !known.Contains(req.Feature))
                {
                    errors["feature"] = ["Unknown feature. See GET /api/platform/licensing/features."];
                }

                var now = clock.GetUtcNow();
                var from = req.ValidFrom ?? now;
                if (req.ValidTo is { } to && to <= from)
                {
                    errors["validTo"] = ["validTo must be after validFrom."];
                }

                if (req.GraceDays is < 0 or > 365)
                {
                    errors["graceDays"] = ["Between 0 and 365 days."];
                }

                if (req.Source is not (null or "manual" or "trial" or "subscription"))
                {
                    errors["source"] = ["manual, trial or subscription."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var e = await db.Entitlements.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Feature == req.Feature, ct);
                if (e is null)
                {
                    e = new Entitlement { TenantId = tenantId, Feature = req.Feature };
                    db.Entitlements.Add(e);
                }

                e.ValidFrom = from;
                e.ValidTo = req.ValidTo;
                e.GraceDays = req.GraceDays ?? 30;
                e.Source = req.Source ?? "manual";
                e.UpdatedAt = now;
                e.UpdatedBy = ctx.UserId;

                await db.SaveChangesWithAuditAsync(new AuditRecord("core.licensing.entitlement.set", "tenant", tenantId.ToString(),
                    Details: new Dictionary<string, object?> { ["feature"] = e.Feature, ["validFrom"] = e.ValidFrom, ["validTo"] = e.ValidTo, ["graceDays"] = e.GraceDays, ["source"] = e.Source }), ct);
                return Results.Ok(ToDto(e, now));
            })
            .RequirePermission("core.licensing.entitlements.manage");

        endpoints.MapGroup("/api/core/licensing").WithTags("core.licensing")
            .MapGet("/entitlements", async (LicensingDbContext db, TimeProvider clock, CancellationToken ct) =>
                TypedResults.Ok((await db.Entitlements.AsNoTracking().OrderBy(e => e.Feature).ToListAsync(ct)).Select(e => ToDto(e, clock.GetUtcNow()))))
            .RequirePermission("core.licensing.entitlements.read");
    }

    private static EntitlementDto ToDto(Entitlement e, DateTimeOffset now) =>
        new(e.Feature, EntitlementRules.Evaluate(e.Feature, e, now).State.ToString(), e.ValidFrom, e.ValidTo, e.GraceDays, e.Source);
}
