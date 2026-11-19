namespace HealthOS.Core.Licensing.Contracts;

public enum EntitlementState
{
    /// <summary>Never entitled: the feature does not exist for this tenant (APIs return 404).</summary>
    None,

    /// <summary>Licensed and usable.</summary>
    Active,

    /// <summary>Expired (in or after grace): data stays readable and exportable, writes are blocked.</summary>
    ReadOnly,
}

public sealed record EntitlementStatus(string Feature, EntitlementState State, DateTimeOffset? ValidTo, DateTimeOffset? GraceEndsAt);

/// <summary>Entitlement checks for the active tenant of the current request.</summary>
public interface IEntitlements
{
    Task<EntitlementStatus> GetAsync(string feature, CancellationToken cancellationToken);
}

public static class LicenseFeatures
{
    public static string Key(string productCode, string feature) => $"{productCode}:{feature}";
}
