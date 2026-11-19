namespace HealthOS.Core.Identity.Contracts;

/// <summary>The authenticated principal as seen by HealthOS. Tenant membership is added in M2.</summary>
/// <param name="UserId">Internal HealthOS user id; null only if provisioning has not happened for this session.</param>
public sealed record CurrentUser(
    Guid? UserId,
    string SubjectId,
    string? DisplayName,
    string? Email,
    IReadOnlyList<string> AuthenticationMethods,
    bool MultiFactorAuthenticated);

/// <summary>Identity asserted by the external identity provider at sign-in.</summary>
public sealed record ExternalIdentity(string Issuer, string Subject, string? DisplayName, string? Email);

/// <summary>
/// Maps identity-provider subjects to stable internal user ids (just-in-time provisioning).
/// Users are global; tenant membership is a separate concept (M2).
/// </summary>
public interface IUserDirectory
{
    Task<Guid> EnsureUserAsync(ExternalIdentity identity, CancellationToken cancellationToken);
}

public static class HealthOsClaimTypes
{
    /// <summary>Internal HealthOS user id added to the session at sign-in.</summary>
    public const string UserId = "hos_uid";
}
