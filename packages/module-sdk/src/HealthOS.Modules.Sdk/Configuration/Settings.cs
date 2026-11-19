using System.Text.Json;
using System.Text.Json.Serialization;

namespace HealthOS.Modules.Sdk.Configuration;

/// <summary>Configuration levels, least to most specific (spec §4). Order matters.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConfigScope>))]
public enum ConfigScope
{
    Global = 0,
    Country = 1,
    Industry = 2,
    Specialty = 3,
    Organization = 4,
    Site = 5,
    Role = 6,
    User = 7,
}

[JsonConverter(typeof(JsonStringEnumConverter<SettingType>))]
public enum SettingType
{
    Boolean,
    Integer,
    Decimal,
    String,
    Enum,
}

/// <summary>How values from several roles of the same user are combined.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MergeStrategy>))]
public enum MergeStrategy
{
    /// <summary>Smallest number / false wins (default: most restrictive for typical limits and switches).</summary>
    MostRestrictive,

    /// <summary>Largest number / true wins.</summary>
    MostPermissive,

    /// <summary>First role in the user's role order wins (for non-ordered values such as text).</summary>
    FirstRole,
}

/// <summary>A setting declared by a module in module.json ("settings").</summary>
public sealed record SettingDefinition
{
    public required string Key { get; init; }
    public required SettingType Type { get; init; }
    public required string Description { get; init; }
    public required JsonElement Default { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public int? MaxLength { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public IReadOnlyList<ConfigScope> AllowedScopes { get; init; } = [ConfigScope.Global];
    public MergeStrategy Merge { get; init; } = MergeStrategy.MostRestrictive;
    public string? Unit { get; init; }
}

/// <summary>A value for one setting at one level. Profiles (country/industry/specialty) and tenant drafts contain these.</summary>
public sealed record SettingEntry
{
    public required string Key { get; init; }
    public required ConfigScope Scope { get; init; }

    /// <summary>Which country/industry/specialty/organization/site/role/user this applies to; empty for Global.</summary>
    public string ScopeKey { get; init; } = string.Empty;

    /// <summary>The value; omitted when the entry only locks or narrows constraints.</summary>
    public JsonElement? Value { get; init; }

    /// <summary>More specific levels cannot override a locked value.</summary>
    public bool Locked { get; init; }

    /// <summary>Narrows the allowed range for more specific levels without fixing the value.</summary>
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }

    public DateTimeOffset? EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
}

/// <summary>Who is asking: determines which entries apply.</summary>
public sealed record ConfigContext
{
    public string? Country { get; init; }
    public string? Industry { get; init; }
    public string? Specialty { get; init; }
    public string? OrganizationId { get; init; }
    public string? SiteId { get; init; }

    /// <summary>Role codes in priority order (used by <see cref="MergeStrategy.FirstRole"/>).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string? UserId { get; init; }
    public required DateTimeOffset Now { get; init; }

    public bool Matches(SettingEntry e) => e.Scope switch
    {
        ConfigScope.Global => true,
        ConfigScope.Country => Eq(Country, e.ScopeKey),
        ConfigScope.Industry => Eq(Industry, e.ScopeKey),
        ConfigScope.Specialty => Eq(Specialty, e.ScopeKey),
        ConfigScope.Organization => Eq(OrganizationId, e.ScopeKey),
        ConfigScope.Site => Eq(SiteId, e.ScopeKey),
        ConfigScope.Role => Roles.Contains(e.ScopeKey, StringComparer.OrdinalIgnoreCase),
        ConfigScope.User => Eq(UserId, e.ScopeKey),
        _ => false,
    };

    private static bool Eq(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One step of how a value was decided (shown in the admin UI: "why is this value 7?").</summary>
public sealed record ResolutionStep(ConfigScope Scope, string ScopeKey, string Effect, string? Value);

public sealed record ResolvedSetting(
    string Key,
    JsonElement Value,
    ConfigScope Source,
    string SourceKey,
    ConfigScope? LockedAt,
    decimal? Min,
    decimal? Max,
    IReadOnlyList<ResolutionStep> Trace,
    IReadOnlyList<string> Problems);
