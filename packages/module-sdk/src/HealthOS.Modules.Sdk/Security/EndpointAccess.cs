using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HealthOS.Modules.Sdk.Security;

/// <summary>
/// Every HTTP endpoint must declare exactly one access rule. An architecture test enumerates all
/// endpoints of the running host and fails if any endpoint has no declaration (deny-by-default design).
/// </summary>
public abstract record EndpointAccess;

/// <summary>Reachable without authentication (health probes, login redirects). Requires a written justification.</summary>
public sealed record PublicAccess(string Justification) : EndpointAccess;

/// <summary>Any authenticated user (e.g. reading one's own profile). Requires a written justification.</summary>
public sealed record AuthenticatedAccess(string Justification) : EndpointAccess;

/// <summary>Requires a permission declared in a module manifest.</summary>
public sealed record PermissionAccess(string Permission) : EndpointAccess;

public static class EndpointAccessExtensions
{
    public const string PermissionPolicyPrefix = "perm:";

    public static TBuilder AllowPublic<TBuilder>(this TBuilder builder, string justification)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        return builder.WithMetadata(new PublicAccess(justification)).AllowAnonymous();
    }

    public static TBuilder RequireAuthenticatedUser<TBuilder>(this TBuilder builder, string justification)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        return builder.WithMetadata(new AuthenticatedAccess(justification)).RequireAuthorization();
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return builder.WithMetadata(new PermissionAccess(permission))
            .RequireAuthorization(PermissionPolicyPrefix + permission);
    }
}
