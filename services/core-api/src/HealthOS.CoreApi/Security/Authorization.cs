using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HealthOS.CoreApi.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Decides whether a principal holds a permission (in the current tenant/site scope).</summary>
public interface IPermissionEvaluator
{
    Task<bool> HasPermissionAsync(System.Security.Claims.ClaimsPrincipal user, string permission, CancellationToken cancellationToken);
}

/// <summary>
/// M1 evaluator: no role/permission store exists yet (M2), so every permission check is denied.
/// Fail-closed by design; replaced in M2-06.
/// </summary>
public sealed class DenyAllPermissionEvaluator : IPermissionEvaluator
{
    public Task<bool> HasPermissionAsync(System.Security.Claims.ClaimsPrincipal user, string permission, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

public sealed class PermissionAuthorizationHandler(IPermissionEvaluator evaluator) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            await evaluator.HasPermissionAsync(context.User, requirement.Permission, CancellationToken.None).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Builds "perm:&lt;code&gt;" policies on demand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(EndpointAccessExtensions.PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[EndpointAccessExtensions.PermissionPolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
