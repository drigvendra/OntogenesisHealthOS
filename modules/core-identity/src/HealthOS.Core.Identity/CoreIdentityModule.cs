using System.Security.Claims;
using System.Text.RegularExpressions;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.Core.Identity;

public sealed record SelectTenantRequest(Guid TenantId);

public sealed record AddMemberRequest(string Email);

public sealed record CreateRoleRequest(string Code, string Name, IReadOnlyList<string> Permissions);

public sealed record AssignRoleRequest(Guid RoleId, Guid? SiteId);

public sealed record AppointTenantAdminRequest(string Email);

public sealed record MemberDto(Guid UserId, string? DisplayName, string? Email, string Status, IReadOnlyList<string> Roles);

public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record PermissionDto(string Code, string Description, bool RequiresMfa);

public sealed partial class CoreIdentityModule : HealthOsModule
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex RoleCode();

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<IdentityDbContext>(Manifest.Id, IdentityDbContext.SchemaName);
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<AccessControl>();
        services.AddScoped<IAccessControl>(sp => sp.GetRequiredService<AccessControl>());
        services.AddScoped<IOperatorAdministration>(sp => sp.GetRequiredService<AccessControl>());
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<HealthOS.Modules.Sdk.Configuration.IUserRoles, UserRoles>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapSelfEndpoints(endpoints.MapGroup("/api/core/identity").WithTags("core.identity"));
        MapTenantAdminEndpoints(endpoints.MapGroup("/api/core/identity").WithTags("core.identity"));
        MapPlatformEndpoints(endpoints.MapGroup("/api/platform").WithTags("platform.identity"));
        BreakGlassEndpoints.Map(endpoints);
    }

    private static void MapSelfEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/me", async (ClaimsPrincipal user, IRequestContext ctx, IAccessControl access, CancellationToken ct) =>
            {
                var isOperator = ctx.UserId is { } uid && await access.IsPlatformOperatorAsync(uid, ct);
                return TypedResults.Ok(ToCurrentUser(user, ctx, isOperator));
            })
            .RequireAuthenticatedUser("Every signed-in user may read their own identity.")
            .WithName("GetCurrentUser");

        g.MapGet("/me/tenants", async (IRequestContext ctx, IAccessControl access, ITenantDirectory tenants, CancellationToken ct) =>
            {
                if (ctx.UserId is not { } uid)
                {
                    return Results.Ok(Array.Empty<TenantSummary>());
                }

                var ids = await access.GetActiveTenantIdsAsync(uid, ct);
                return Results.Ok((await tenants.GetManyAsync(ids, ct)).Where(t => t.IsActive));
            })
            .RequireAuthenticatedUser("Every signed-in user may list the tenants they belong to.");

        g.MapPut("/session/tenant", async (SelectTenantRequest req, IRequestContext ctx, IAccessControl access, ITenantDirectory tenants, ISessionManager session, CancellationToken ct) =>
            {
                if (ctx.UserId is not { } uid ||
                    !await access.HasTenantAccessAsync(uid, req.TenantId, ct) ||
                    await tenants.FindAsync(req.TenantId, ct) is not { IsActive: true })
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You are not an active member of this tenant.");
                }

                await session.SetActiveTenantAsync(req.TenantId);
                return Results.NoContent();
            })
            .RequireAuthenticatedUser("Users choose one of their own tenants; membership is verified.");
    }

    private static void MapTenantAdminEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/permissions", (PermissionCatalog catalog) =>
                TypedResults.Ok(catalog.TenantScoped.OrderBy(p => p.Code).Select(p => new PermissionDto(p.Code, p.Description, p.RequiresMfa))))
            .RequirePermission("core.identity.users.read");

        g.MapGet("/members", async (IdentityDbContext db, IRequestContext ctx, CancellationToken ct) =>
            {
                var tenantId = ctx.TenantId!.Value;
                var members = await (from m in db.Memberships.AsNoTracking().Where(m => m.TenantId == tenantId)
                                     join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                                     orderby u.DisplayName
                                     select new { m.UserId, u.DisplayName, u.Email, m.Status }).ToListAsync(ct);
                var roles = await (from a in db.RoleAssignments.AsNoTracking()
                                   join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                                   select new { a.UserId, r.Code }).ToListAsync(ct);
                var byUser = roles.ToLookup(x => x.UserId, x => x.Code);
                return TypedResults.Ok(members.Select(m => new MemberDto(m.UserId, m.DisplayName, m.Email, m.Status, byUser[m.UserId].Distinct().Order().ToList())));
            })
            .RequirePermission("core.identity.users.read");

        g.MapPost("/members", async (AddMemberRequest req, AccessControl access, IdentityDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Email) || await access.FindUserByEmailAsync(req.Email, ct) is not { } user)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["No user with this email has signed in yet."] });
                }

                if (await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.TenantId == ctx.TenantId, ct))
                {
                    return Results.Conflict(new { title = "Already a member." });
                }

                db.Memberships.Add(new TenantMembership { TenantId = ctx.TenantId!.Value, UserId = user.Id, CreatedAt = clock.GetUtcNow() });
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.identity.member.added", "user", user.Id.ToString()), ct);
                return Results.Created($"/api/core/identity/members/{user.Id}", new MemberDto(user.Id, user.DisplayName, user.Email, MembershipStatus.Active, []));
            })
            .RequirePermission("core.identity.users.manage");

        g.MapGet("/roles", async (IdentityDbContext db, CancellationToken ct) =>
            {
                var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Code).ToListAsync(ct);
                var perms = (await db.RolePermissions.AsNoTracking().ToListAsync(ct)).ToLookup(p => p.RoleId, p => p.Permission);
                return TypedResults.Ok(roles.Select(r => new RoleDto(r.Id, r.Code, r.Name, r.IsSystem, perms[r.Id].Order().ToList())));
            })
            .RequirePermission("core.identity.users.read");

        g.MapPost("/roles", async (CreateRoleRequest req, IdentityDbContext db, PermissionCatalog catalog, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(req.Code) || !RoleCode().IsMatch(req.Code) || req.Code == AccessControl.TenantAdminRoleCode)
                {
                    errors["code"] = ["Use 2-64 lowercase letters, digits or hyphens; 'tenant-admin' is reserved."];
                }

                if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 128)
                {
                    errors["name"] = ["Name is required (max 128 characters)."];
                }

                // Only tenant-scoped permissions can be put in tenant roles: no escalation to platform rights.
                var invalid = (req.Permissions ?? []).Where(p => catalog.Find(p) is not { Scope: PermissionScope.Tenant }).ToList();
                if (invalid.Count > 0)
                {
                    errors["permissions"] = [$"Unknown or non-tenant permissions: {string.Join(", ", invalid)}"];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var tenantId = ctx.TenantId!.Value;
                var role = new Role { Id = Guid.CreateVersion7(), TenantId = tenantId, Code = req.Code, Name = req.Name.Trim(), CreatedAt = clock.GetUtcNow() };
                db.Roles.Add(role);
                foreach (var p in req.Permissions!.Distinct(StringComparer.Ordinal))
                {
                    db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = role.Id, Permission = p });
                }

                try
                {
                    await db.SaveChangesWithAuditAsync(new AuditRecord("core.identity.role.created", "role", role.Id.ToString(),
                        Details: new Dictionary<string, object?> { ["code"] = role.Code, ["permissions"] = req.Permissions!.Distinct().Order().ToArray() }), ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Results.Conflict(new { title = $"Role code '{req.Code}' is already in use." });
                }

                return Results.Created($"/api/core/identity/roles/{role.Id}", new RoleDto(role.Id, role.Code, role.Name, false, req.Permissions!.Distinct().Order().ToList()));
            })
            .RequirePermission("core.identity.roles.manage");

        g.MapPost("/members/{userId:guid}/roles", async (Guid userId, AssignRoleRequest req, IdentityDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                // Row-level security limits both lookups to the active tenant.
                if (!await db.Memberships.AnyAsync(m => m.UserId == userId && m.TenantId == ctx.TenantId, ct) || !await db.Roles.AnyAsync(r => r.Id == req.RoleId, ct))
                {
                    return Results.NotFound();
                }

                db.RoleAssignments.Add(new RoleAssignment
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = ctx.TenantId!.Value,
                    UserId = userId,
                    RoleId = req.RoleId,
                    SiteId = req.SiteId,
                    CreatedAt = clock.GetUtcNow(),
                });
                try
                {
                    await db.SaveChangesWithAuditAsync(new AuditRecord("core.identity.role.assigned", "user", userId.ToString(),
                        Details: new Dictionary<string, object?> { ["roleId"] = req.RoleId, ["siteId"] = req.SiteId }), ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Results.Conflict(new { title = "Role already assigned." });
                }

                return Results.NoContent();
            })
            .RequirePermission("core.identity.roles.manage");
    }

    private static void MapPlatformEndpoints(RouteGroupBuilder g)
    {
        g.MapPost("/tenants/{tenantId:guid}/admins", async (Guid tenantId, AppointTenantAdminRequest req, AccessControl access, ITenantDirectory tenants, CancellationToken ct) =>
            {
                if (await tenants.FindAsync(tenantId, ct) is null)
                {
                    return Results.NotFound();
                }

                if (string.IsNullOrWhiteSpace(req.Email) || await access.FindUserByEmailAsync(req.Email, ct) is not { } user)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["No user with this email has signed in yet."] });
                }

                await access.GrantTenantAdminAsync(tenantId, user.Id, ct);
                return Results.NoContent();
            })
            .RequirePermission("core.identity.tenant-admins.manage");

        g.MapGet("/operators", async (IdentityDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await (from o in db.PlatformOperators.AsNoTracking()
                                       join u in db.Users.AsNoTracking() on o.UserId equals u.Id
                                       orderby u.DisplayName
                                       select new { o.UserId, u.DisplayName, u.Email, o.GrantedAt }).ToListAsync(ct)))
            .RequirePermission("core.identity.operators.read");
    }

    internal static CurrentUser ToCurrentUser(ClaimsPrincipal user, IRequestContext ctx, bool isOperator)
    {
        var amr = user.FindAll("amr").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        return new CurrentUser(
            UserId: ctx.UserId,
            SubjectId: user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            DisplayName: user.FindFirstValue("name") ?? user.Identity?.Name,
            Email: user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email),
            AuthenticationMethods: amr,
            MultiFactorAuthenticated: HealthOsClaims.HasMultiFactor(user),
            ActiveTenantId: ctx.TenantId,
            IsPlatformOperator: isOperator);
    }
}
