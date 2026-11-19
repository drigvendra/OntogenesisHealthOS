using System.Security.Claims;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HealthOS.CoreApi.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed partial class PermissionAuthorizationHandler(IHttpContextAccessor http, ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Permission {Permission} denied: {Reason}")]
    private static partial void LogDenied(ILogger logger, string permission, string reason);

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || http.HttpContext is not { } httpContext)
        {
            return;
        }

        var evaluator = httpContext.RequestServices.GetRequiredService<IPermissionEvaluator>();
        var decision = await evaluator.EvaluateAsync(context.User, requirement.Permission, httpContext.RequestAborted).ConfigureAwait(false);
        if (decision.Granted)
        {
            context.Succeed(requirement);
        }
        else
        {
            LogDenied(logger, requirement.Permission, decision.Reason);
            await AuditDenialAsync(httpContext, requirement.Permission, decision.Reason).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not write permission denial to the audit trail")]
    private static partial void LogAuditFailure(ILogger logger, Exception exception);

    /// <summary>Denials are recorded in the tenant chain (or the platform chain on platform endpoints).</summary>
    private async Task AuditDenialAsync(HttpContext httpContext, string permission, string reason)
    {
        var ctx = httpContext.RequestServices.GetRequiredService<IRequestContext>();
        if (ctx.UserId is null || (ctx.TenantId is null && !ctx.IsPlatformScope))
        {
            return; // no chain to write to; the structured log above still records it
        }

        try
        {
            await httpContext.RequestServices.GetRequiredService<HealthOS.Core.Audit.IAuditLog>().RecordAsync(
                new HealthOS.Platform.Data.AuditRecord("core.security.permission.denied", "permission", permission, HealthOS.Platform.Data.AuditOutcome.Denied,
                    new Dictionary<string, object?> { ["reason"] = reason, ["path"] = httpContext.Request.Path.Value }),
                httpContext.RequestAborted).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed denial audit must not turn a 403 into a 500; it is logged as an error.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAuditFailure(logger, ex);
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

/// <summary>
/// Builds the request context from the authenticated session. The tenant comes only from the session
/// claim and is re-validated (active membership, active tenant) on every API request, so revoking a
/// membership or suspending a tenant takes effect immediately. /api/platform/* runs in platform scope.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext ctx)
    {
        var user = http.User;
        if (http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
            user.Identity?.IsAuthenticated == true &&
            Guid.TryParse(user.FindFirstValue(HealthOsClaims.UserId), out var userId))
        {
            ctx.SetUser(userId);
            if (http.Request.Path.StartsWithSegments("/api/platform", StringComparison.OrdinalIgnoreCase))
            {
                ctx.EnterPlatformScope();
            }
            else if (Guid.TryParse(user.FindFirstValue(HealthOsClaims.TenantId), out var tenantId))
            {
                var access = http.RequestServices.GetRequiredService<IAccessControl>();
                var tenants = http.RequestServices.GetRequiredService<ITenantDirectory>();
                if (await access.HasTenantAccessAsync(userId, tenantId, http.RequestAborted).ConfigureAwait(false) &&
                    await tenants.FindAsync(tenantId, http.RequestAborted).ConfigureAwait(false) is { IsActive: true })
                {
                    ctx.SetTenant(tenantId);
                }
            }
        }

        await next(http).ConfigureAwait(false);
    }
}

/// <summary>Re-issues the session cookie with a new active tenant claim.</summary>
public sealed class CookieSessionManager(IHttpContextAccessor accessor) : ISessionManager
{
    public async Task SetActiveTenantAsync(Guid? tenantId)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");
        var result = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal.Identity is not ClaimsIdentity current)
        {
            throw new InvalidOperationException("No cookie session to update.");
        }

        var identity = new ClaimsIdentity(
            result.Principal.Claims.Where(c => c.Type != HealthOsClaims.TenantId),
            current.AuthenticationType, current.NameClaimType, current.RoleClaimType);
        if (tenantId is { } t)
        {
            identity.AddClaim(new Claim(HealthOsClaims.TenantId, t.ToString()));
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), result.Properties).ConfigureAwait(false);
    }
}
