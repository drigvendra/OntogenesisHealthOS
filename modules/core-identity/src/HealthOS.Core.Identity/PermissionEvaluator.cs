using System.Security.Claims;
using HealthOS.Core.Identity.Data;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Identity;

/// <summary>
/// Request-scoped permission decisions:
/// - platform permissions: only in platform scope (/api/platform/*), only platform operators, always MFA;
/// - tenant permissions: active tenant + tenant-wide role assignment containing the permission, MFA when declared;
/// - unknown permissions: denied.
/// Site-scoped assignments are stored but not yet used for decisions (ABAC by site arrives with site-bound resources).
/// </summary>
internal sealed class PermissionEvaluator(IRequestContext context, PermissionCatalog catalog, IdentityDbContext db) : IPermissionEvaluator
{
    private HashSet<string>? _tenantPermissions;
    private bool? _isOperator;

    public async Task<PermissionDecision> EvaluateAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var declaration = catalog.Find(permission);
        if (declaration is null)
        {
            return PermissionDecision.Deny("unknown permission");
        }

        if (context.UserId is not { } userId)
        {
            return PermissionDecision.Deny("no provisioned user");
        }

        var mfa = HealthOsClaims.HasMultiFactor(user);

        if (declaration.Scope == PermissionScope.Platform)
        {
            if (!context.IsPlatformScope)
            {
                return PermissionDecision.Deny("platform permission outside platform scope");
            }

            _isOperator ??= await db.PlatformOperators.AsNoTracking().AnyAsync(o => o.UserId == userId, cancellationToken).ConfigureAwait(false);
            if (!_isOperator.Value)
            {
                return PermissionDecision.Deny("not a platform operator");
            }

            return mfa ? PermissionDecision.Allow("platform operator") : PermissionDecision.Deny("multi-factor authentication required");
        }

        if (context.IsPlatformScope || context.TenantId is null)
        {
            return PermissionDecision.Deny("no active tenant");
        }

        _tenantPermissions ??= (await (
                from a in db.RoleAssignments.AsNoTracking()
                join p in db.RolePermissions.AsNoTracking() on a.RoleId equals p.RoleId
                where a.UserId == userId && a.SiteId == null
                select p.Permission)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        if (!_tenantPermissions.Contains(permission))
        {
            return PermissionDecision.Deny("permission not granted in tenant");
        }

        if (declaration.RequiresMfa && !mfa)
        {
            return PermissionDecision.Deny("multi-factor authentication required");
        }

        return PermissionDecision.Allow("granted by tenant role");
    }
}
