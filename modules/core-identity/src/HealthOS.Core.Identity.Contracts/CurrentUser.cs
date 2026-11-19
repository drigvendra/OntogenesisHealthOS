namespace HealthOS.Core.Identity.Contracts;

/// <summary>The authenticated principal as seen by HealthOS.</summary>
/// <param name="UserId">Internal HealthOS user id; null only if provisioning has not happened for this session.</param>
/// <param name="ActiveTenantId">Tenant selected for this session (validated membership), if any.</param>
public sealed record CurrentUser(
    Guid? UserId,
    string SubjectId,
    string? DisplayName,
    string? Email,
    IReadOnlyList<string> AuthenticationMethods,
    bool MultiFactorAuthenticated,
    Guid? ActiveTenantId,
    bool IsPlatformOperator);

/// <summary>Identity asserted by the external identity provider at sign-in.</summary>
public sealed record ExternalIdentity(string Issuer, string Subject, string? DisplayName, string? Email);

/// <summary>
/// Maps identity-provider subjects to stable internal user ids (just-in-time provisioning).
/// Users are global; tenant membership is separate and tenant-scoped.
/// </summary>
public interface IUserDirectory
{
    Task<Guid> EnsureUserAsync(ExternalIdentity identity, CancellationToken cancellationToken);
}

/// <summary>Membership and operator checks used by the host to build the request context.</summary>
public interface IAccessControl
{
    /// <summary>Tenants where the user has an active membership (reads the user's own memberships only).</summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Active membership, or an approved and unexpired break-glass grant (read-only access).</summary>
    Task<bool> HasTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken);
}
