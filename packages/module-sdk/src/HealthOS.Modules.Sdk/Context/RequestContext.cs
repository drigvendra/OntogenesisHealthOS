namespace HealthOS.Modules.Sdk.Context;

/// <summary>
/// Who is acting and in which tenant, for the current request or job. Derived only from the
/// authenticated session on the server; never from client-supplied headers or bodies.
/// The data layer copies it into the PostgreSQL session, where row-level security enforces it.
/// </summary>
public interface IRequestContext
{
    Guid? UserId { get; }

    /// <summary>Active tenant; null means no tenant data is visible (fail closed).</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// True only on platform-operator endpoints (/api/platform/*). Policies of platform-administered
    /// tables (tenancy, identity, licensing) may allow cross-tenant access in this scope; tables with
    /// patient or regulated data never do.
    /// </summary>
    bool IsPlatformScope { get; }
}

public sealed class RequestContext : IRequestContext
{
    public Guid? UserId { get; private set; }

    public Guid? TenantId { get; private set; }

    public bool IsPlatformScope { get; private set; }

    public void SetUser(Guid? userId) => UserId = userId;

    public void SetTenant(Guid? tenantId)
    {
        TenantId = tenantId;
        if (tenantId is not null)
        {
            IsPlatformScope = false;
        }
    }

    public void EnterPlatformScope()
    {
        IsPlatformScope = true;
        TenantId = null;
    }

    /// <summary>Runs work in another tenant's context (e.g. provisioning a new tenant), then restores.</summary>
    public async Task<T> RunAsTenantAsync<T>(Guid tenantId, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var (prevTenant, prevPlatform) = (TenantId, IsPlatformScope);
        TenantId = tenantId;
        IsPlatformScope = false;
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            TenantId = prevTenant;
            IsPlatformScope = prevPlatform;
        }
    }
}
