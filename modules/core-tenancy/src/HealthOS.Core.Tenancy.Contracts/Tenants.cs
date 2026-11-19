namespace HealthOS.Core.Tenancy.Contracts;

public sealed record TenantSummary(Guid Id, string Code, string Name, string RegionCell, string Status)
{
    public bool IsActive => Status == TenantStatus.Active;
}

public static class TenantStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

/// <summary>Read access to the tenant directory for other platform modules.</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> FindAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantSummary>> GetManyAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken);
}
