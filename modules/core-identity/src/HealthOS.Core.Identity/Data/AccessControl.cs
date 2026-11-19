using HealthOS.Core.Identity.Contracts;
using HealthOS.Modules.Sdk.Security;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Identity.Data;

/// <summary>Used by the host's operator CLI (database access required; never exposed over HTTP).</summary>
public interface IOperatorAdministration
{
    /// <returns>False if no user with that email has signed in yet.</returns>
    Task<bool> GrantOperatorByEmailAsync(string email, CancellationToken ct);
}

internal sealed class AccessControl(IdentityDbContext db, PermissionCatalog catalog, TimeProvider clock) : IAccessControl, IOperatorAdministration
{
    public async Task<bool> GrantOperatorByEmailAsync(string email, CancellationToken ct)
    {
        if (await FindUserByEmailAsync(email, ct).ConfigureAwait(false) is not { } user)
        {
            return false;
        }

        await GrantPlatformOperatorAsync(user.Id, grantedBy: null, ct).ConfigureAwait(false);
        return true;
    }

    public const string TenantAdminRoleCode = "tenant-admin";

    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.TenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        db.Memberships.AsNoTracking().AnyAsync(
            m => m.UserId == userId && m.TenantId == tenantId && m.Status == MembershipStatus.Active, cancellationToken);

    public Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken) =>
        db.PlatformOperators.AsNoTracking().AnyAsync(o => o.UserId == userId, cancellationToken);

    /// <summary>
    /// Makes a user administrator of a tenant: membership, the system "tenant-admin" role with every
    /// tenant-scoped permission in this package (kept in sync on every call), and the assignment.
    /// Runs in platform scope. Idempotent.
    /// </summary>
    public async Task GrantTenantAdminAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (!await db.Memberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct).ConfigureAwait(false))
        {
            db.Memberships.Add(new TenantMembership { TenantId = tenantId, UserId = userId, CreatedAt = now });
        }

        var role = await db.Roles.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Code == TenantAdminRoleCode, ct).ConfigureAwait(false);
        if (role is null)
        {
            role = new Role { Id = Guid.CreateVersion7(now), TenantId = tenantId, Code = TenantAdminRoleCode, Name = "Tenant administrator", IsSystem = true, CreatedAt = now };
            db.Roles.Add(role);
        }

        var existing = await db.RolePermissions.Where(p => p.RoleId == role.Id).Select(p => p.Permission).ToListAsync(ct).ConfigureAwait(false);
        foreach (var p in catalog.TenantScoped.Select(p => p.Code).Except(existing, StringComparer.Ordinal))
        {
            db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = role.Id, Permission = p });
        }

        if (!await db.RoleAssignments.AnyAsync(a => a.TenantId == tenantId && a.UserId == userId && a.RoleId == role.Id && a.SiteId == null, ct).ConfigureAwait(false))
        {
            db.RoleAssignments.Add(new RoleAssignment { Id = Guid.CreateVersion7(now), TenantId = tenantId, UserId = userId, RoleId = role.Id, CreatedAt = now });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task GrantPlatformOperatorAsync(Guid userId, Guid? grantedBy, CancellationToken ct)
    {
        if (!await db.PlatformOperators.AnyAsync(o => o.UserId == userId, ct).ConfigureAwait(false))
        {
            db.PlatformOperators.Add(new PlatformOperator { UserId = userId, GrantedAt = clock.GetUtcNow(), GrantedBy = grantedBy });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public Task<UserAccount?> FindUserByEmailAsync(string email, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.Trim().ToLower(), ct);
}
