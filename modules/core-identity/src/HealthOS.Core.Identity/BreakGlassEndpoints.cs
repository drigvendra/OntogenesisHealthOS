using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Identity;

public sealed record BreakGlassRequest(string Reason, int DurationMinutes);

public sealed record BreakGlassDto(Guid Id, Guid TenantId, Guid UserId, string Reason, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, Guid? ApprovedBy, bool Active);

/// <summary>
/// Break-glass: an operator requests time-boxed (max 4 h) read-only access to one tenant with a
/// reason; a different operator must approve. Request and approval are recorded in both the platform
/// chain and the tenant's own audit chain, so the tenant can see who accessed their data and why.
/// </summary>
internal static class BreakGlassEndpoints
{
    public const int MaxMinutes = 240;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform").WithTags("platform.break-glass");

        platform.MapPost("/tenants/{tenantId:guid}/break-glass", async (Guid tenantId, BreakGlassRequest req, IdentityDbContext db,
                ITenantDirectory tenants, RequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length < 10 || req.Reason.Length > 500)
                {
                    errors["reason"] = ["Describe the reason (10-500 characters), e.g. the support ticket."];
                }

                if (req.DurationMinutes is < 5 or > MaxMinutes)
                {
                    errors["durationMinutes"] = [$"Between 5 and {MaxMinutes} minutes."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                if (await tenants.FindAsync(tenantId, ct) is null)
                {
                    return Results.NotFound();
                }

                var now = clock.GetUtcNow();
                var grant = new BreakGlassGrant
                {
                    Id = Guid.CreateVersion7(now),
                    TenantId = tenantId,
                    UserId = ctx.UserId!.Value,
                    Reason = req.Reason.Trim(),
                    RequestedAt = now,
                    ExpiresAt = now.AddMinutes(req.DurationMinutes),
                };
                db.BreakGlassGrants.Add(grant);
                var record = new AuditRecord("core.identity.break-glass.requested", "break-glass", grant.Id.ToString(),
                    Details: new Dictionary<string, object?> { ["tenantId"] = tenantId, ["minutes"] = req.DurationMinutes });
                await db.SaveChangesWithAuditAsync(record, ct);
                await ctx.RunAsTenantAsync(tenantId, async () => { await db.AppendAuditAsync(record, ct); return 0; });

                return Results.Created($"/api/platform/break-glass/{grant.Id}", ToDto(grant, now));
            })
            .RequirePermission("core.identity.break-glass.request");

        platform.MapPost("/break-glass/{grantId:guid}/approve", async (Guid grantId, IdentityDbContext db, RequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var grant = await db.BreakGlassGrants.SingleOrDefaultAsync(g => g.Id == grantId, ct);
                var now = clock.GetUtcNow();
                if (grant is null)
                {
                    return Results.NotFound();
                }

                if (grant.UserId == ctx.UserId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A second operator must approve break-glass access.");
                }

                if (grant.ApprovedBy is not null || grant.ExpiresAt <= now)
                {
                    return Results.Conflict(new { title = "This request is already approved or has expired." });
                }

                grant.ApprovedBy = ctx.UserId;
                grant.ApprovedAt = now;
                var record = new AuditRecord("core.identity.break-glass.approved", "break-glass", grant.Id.ToString(),
                    Details: new Dictionary<string, object?> { ["tenantId"] = grant.TenantId, ["operator"] = grant.UserId, ["expiresAt"] = grant.ExpiresAt });
                await db.SaveChangesWithAuditAsync(record, ct);
                await ctx.RunAsTenantAsync(grant.TenantId, async () => { await db.AppendAuditAsync(record, ct); return 0; });
                return Results.Ok(ToDto(grant, now));
            })
            .RequirePermission("core.identity.break-glass.approve");

        platform.MapGet("/break-glass", async (IdentityDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var now = clock.GetUtcNow();
                var rows = await db.BreakGlassGrants.AsNoTracking().OrderByDescending(g => g.RequestedAt).Take(200).ToListAsync(ct);
                return TypedResults.Ok(rows.Select(g => ToDto(g, now)));
            })
            .RequirePermission("core.identity.break-glass.request");

        // Tenants can see every break-glass grant made for them.
        endpoints.MapGroup("/api/core/identity").WithTags("core.identity")
            .MapGet("/break-glass", async (IdentityDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var now = clock.GetUtcNow();
                var tenantId = ctx.TenantId!.Value;
                var rows = await db.BreakGlassGrants.AsNoTracking().Where(g => g.TenantId == tenantId).OrderByDescending(g => g.RequestedAt).ToListAsync(ct);
                return TypedResults.Ok(rows.Select(g => ToDto(g, now)));
            })
            .RequirePermission("core.identity.users.read");
    }

    private static BreakGlassDto ToDto(BreakGlassGrant g, DateTimeOffset now) =>
        new(g.Id, g.TenantId, g.UserId, g.Reason, g.RequestedAt, g.ExpiresAt, g.ApprovedBy, g.IsActive(now));
}
