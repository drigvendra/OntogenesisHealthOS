namespace HealthOS.Modules.Sdk.Context;

/// <summary>Changes the signed-in session (implemented by the host, which owns the session cookie).</summary>
public interface ISessionManager
{
    /// <summary>Stores the active tenant in the session. Callers must have verified membership.</summary>
    Task SetActiveTenantAsync(Guid? tenantId);
}
