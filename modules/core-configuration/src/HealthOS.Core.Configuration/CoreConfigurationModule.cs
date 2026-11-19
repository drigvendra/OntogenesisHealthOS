using System.Text.Json;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Core.Configuration;

public sealed record SetProfileRequest(string Country, string Industry, string? Specialty);

public sealed record CreateDraftRequest(string Summary, IReadOnlyList<SettingEntry> Entries);

public sealed record EntryProblem(int Index, string Key, string Problem);

public sealed record DraftDto(Guid Id, int BaseVersion, string Summary, string Status, IReadOnlyList<SettingEntry> Entries,
    DateTimeOffset CreatedAt, Guid CreatedBy, Guid? ApprovedBy, DateTimeOffset? ApprovedAt);

public sealed record SnapshotDto(int Version, DateTimeOffset CreatedAt, Guid CreatedBy, Guid? DraftId, int? RolledBackTo, int EntryCount);

public sealed record PreviewRequest(string? OrganizationId, string? SiteId, IReadOnlyList<string>? Roles, string? UserId);

public sealed record RollbackRequest(int ToVersion);

/// <summary>Reads, validates and resolves a tenant's configuration (profile + current snapshot / a draft).</summary>
public sealed class ConfigurationService(ConfigurationDbContext db, SettingsCatalog settings, ProfileCatalog profiles, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Tenants author only organization/site/role/user entries; broader levels come from platform profiles.</summary>
    public static readonly ConfigScope[] TenantScopes = [ConfigScope.Organization, ConfigScope.Site, ConfigScope.Role, ConfigScope.User];

    public Task<TenantProfile?> ProfileAsync(Guid tenantId, CancellationToken ct) =>
        db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task<ConfigSnapshot?> CurrentAsync(Guid tenantId, CancellationToken ct) =>
        await db.Snapshots.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public static IReadOnlyList<SettingEntry> Parse(string json) => JsonSerializer.Deserialize<List<SettingEntry>>(json, Json) ?? [];

    public static string Serialize(IReadOnlyList<SettingEntry> entries) => JsonSerializer.Serialize(entries, Json);

    public IReadOnlyList<EntryProblem> Validate(IReadOnlyList<SettingEntry> entries, TenantProfile? profile)
    {
        var problems = new List<EntryProblem>();
        var profileEntries = profiles.EntriesFor(profile).ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (settings.Find(e.Key) is not { } def)
            {
                problems.Add(new(i, e.Key, "unknown setting"));
                continue;
            }

            if (!TenantScopes.Contains(e.Scope))
            {
                problems.Add(new(i, e.Key, $"{e.Scope} values come from platform profiles and cannot be set by a tenant"));
            }

            problems.AddRange(ConfigResolver.ValidateEntry(def, e, profileEntries).Select(p => new EntryProblem(i, e.Key, p)));
        }

        var duplicates = entries.GroupBy(e => (e.Key, e.Scope, e.ScopeKey.ToUpperInvariant(), e.EffectiveFrom)).Where(g => g.Count() > 1);
        problems.AddRange(duplicates.Select(g => new EntryProblem(-1, g.Key.Key, $"duplicate entry at {g.Key.Scope} '{g.Key.Item3}'")));
        return problems;
    }

    public IReadOnlyList<ResolvedSetting> ResolveAll(IReadOnlyList<SettingEntry> tenantEntries, TenantProfile? profile, ConfigContext context)
    {
        var all = profiles.EntriesFor(profile).Concat(tenantEntries).ToList();
        return settings.All.Select(d => ConfigResolver.Resolve(d, all, context)).ToList();
    }

    public ConfigContext Context(TenantProfile? profile, string? orgId, string? siteId, IReadOnlyList<string> roles, string? userId) => new()
    {
        Country = profile?.Country,
        Industry = profile?.Industry,
        Specialty = profile?.Specialty,
        OrganizationId = orgId,
        SiteId = siteId,
        Roles = roles,
        UserId = userId,
        Now = clock.GetUtcNow(),
    };

    /// <summary>Whether the four-eyes rule applies to this tenant (a setting itself; locked on by the pharma profile).</summary>
    public async Task<bool> FourEyesAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await ProfileAsync(tenantId, ct).ConfigureAwait(false);
        var current = await CurrentAsync(tenantId, ct).ConfigureAwait(false);
        var entries = profiles.EntriesFor(profile).Concat(current is null ? [] : Parse(current.Entries)).ToList();
        var def = settings.Find("core.configuration.approval.fourEyes")!;
        // Tenant-wide question: organization-level entries count, role/user entries do not.
        var context = Context(profile, null, null, [], null);
        var orgValues = entries.Where(e => e.Key == def.Key && e.Scope == ConfigScope.Organization && e.Value?.ValueKind == JsonValueKind.True);
        return ConfigResolver.Resolve(def, entries, context).Value.GetBoolean() || orgValues.Any();
    }
}

/// <summary>Effective settings for the current user (tenant profile + current snapshot + user's roles).</summary>
internal sealed class SettingsReader(ConfigurationService config, SettingsCatalog catalog, ProfileCatalog profiles, IRequestContext ctx, IUserRoles roles) : ISettingsReader
{
    public async Task<ResolvedSetting> GetAsync(string key, string? siteId, CancellationToken cancellationToken)
    {
        var def = catalog.Find(key) ?? throw new KeyNotFoundException($"Unknown setting '{key}'.");
        if (ctx.TenantId is not { } tenantId)
        {
            return ConfigResolver.Resolve(def, [], new ConfigContext { Now = DateTimeOffset.UtcNow });
        }

        var profile = await config.ProfileAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var current = await config.CurrentAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var roleCodes = ctx.UserId is { } uid ? await roles.GetRoleCodesAsync(uid, cancellationToken).ConfigureAwait(false) : [];
        var entries = (current is null ? [] : ConfigurationService.Parse(current.Entries)).ToList();
        var all = new List<SettingEntry>();
        all.AddRange(profiles.EntriesFor(profile));
        all.AddRange(entries);
        return ConfigResolver.Resolve(def, all, config.Context(profile, null, siteId, roleCodes, ctx.UserId?.ToString()));
    }
}

public sealed class CoreConfigurationModule : HealthOsModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<ConfigurationDbContext>(Manifest.Id, ConfigurationDbContext.SchemaName);
        services.AddSingleton<ProfileCatalog>();
        services.AddSingleton(sp => new SettingsCatalog(sp.GetRequiredService<IReadOnlyList<IHealthOsModule>>().Select(m => m.Manifest)));
        services.AddScoped<ConfigurationService>();
        services.AddScoped<ISettingsReader, SettingsReader>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ConfigurationEndpoints.Map(endpoints);
    }
}
