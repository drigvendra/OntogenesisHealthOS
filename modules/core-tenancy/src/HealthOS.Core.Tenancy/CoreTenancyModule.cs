using System.Text.RegularExpressions;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Core.Tenancy.Data;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.Core.Tenancy;

public sealed record CreateTenantRequest(string Code, string Name);

public sealed record CreateOrganizationRequest(string Name);

public sealed record CreateSiteRequest(Guid OrganizationId, string Code, string Name, string TimeZone);

public sealed record CreateDepartmentRequest(string Name);

public sealed record OrganizationDto(Guid Id, string Name);

public sealed record SiteDto(Guid Id, Guid OrganizationId, string Code, string Name, string TimeZone);

public sealed record DepartmentDto(Guid Id, Guid SiteId, string Name);

public sealed class TenancyOptions
{
    /// <summary>Deployment cell (data-residency region) of this installation, e.g. "IN-1" or "EU-1".</summary>
    public string Cell { get; set; } = "LOCAL-1";
}

public sealed partial class CoreTenancyModule : HealthOsModule
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$")]
    private static partial Regex TenantCode();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex SiteCode();

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<TenancyDbContext>(Manifest.Id, TenancyDbContext.SchemaName);
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddOptions<TenancyOptions>().Bind(configuration.GetSection("Platform"));
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapPlatformEndpoints(endpoints.MapGroup("/api/platform/tenants").WithTags("platform.tenants"));
        MapTenantEndpoints(endpoints.MapGroup("/api/core/tenancy").WithTags("core.tenancy"));
    }

    private static void MapPlatformEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/", async (TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok((await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)).Select(t => t.ToSummary())))
            .RequirePermission("core.tenancy.tenants.read");

        g.MapPost("/", async (CreateTenantRequest req, TenancyDbContext db, IRequestContext ctx,
                Microsoft.Extensions.Options.IOptions<TenancyOptions> options, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(req.Code) || !TenantCode().IsMatch(req.Code))
                {
                    errors["code"] = ["Use 2-32 lowercase letters, digits or hyphens, starting with a letter."];
                }

                if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
                {
                    errors["name"] = ["Name is required (max 200 characters)."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var tenant = new Tenant
                {
                    Id = Guid.CreateVersion7(),
                    Code = req.Code,
                    Name = req.Name.Trim(),
                    RegionCell = options.Value.Cell,
                    CreatedAt = clock.GetUtcNow(),
                    CreatedBy = ctx.UserId,
                };
                db.Tenants.Add(tenant);
                try
                {
                    await db.SaveChangesWithAuditAsync(new AuditRecord("core.tenancy.tenant.created", "tenant", tenant.Id.ToString(),
                        Details: new Dictionary<string, object?> { ["code"] = tenant.Code, ["cell"] = tenant.RegionCell }), ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Results.Conflict(new { title = $"Tenant code '{req.Code}' is already in use." });
                }

                return Results.Created($"/api/platform/tenants/{tenant.Id}", tenant.ToSummary());
            })
            .RequirePermission("core.tenancy.tenants.manage");

        g.MapPost("/{tenantId:guid}/suspend", (Guid tenantId, TenancyDbContext db, CancellationToken ct) =>
                SetStatus(db, tenantId, TenantStatus.Suspended, ct))
            .RequirePermission("core.tenancy.tenants.manage");

        g.MapPost("/{tenantId:guid}/activate", (Guid tenantId, TenancyDbContext db, CancellationToken ct) =>
                SetStatus(db, tenantId, TenantStatus.Active, ct))
            .RequirePermission("core.tenancy.tenants.manage");
    }

    private static async Task<IResult> SetStatus(TenancyDbContext db, Guid tenantId, string status, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        tenant.Status = status;
        var action = status == TenantStatus.Active ? "core.tenancy.tenant.activated" : "core.tenancy.tenant.suspended";
        await db.SaveChangesWithAuditAsync(new AuditRecord(action, "tenant", tenant.Id.ToString()), ct);
        return Results.Ok(tenant.ToSummary());
    }

    private static void MapTenantEndpoints(RouteGroupBuilder g)
    {
        g.MapGet("/tenant", async (IRequestContext ctx, ITenantDirectory directory, CancellationToken ct) =>
                await directory.FindAsync(ctx.TenantId!.Value, ct) is { } t ? Results.Ok(t) : Results.NotFound())
            .RequirePermission("core.tenancy.structure.read");

        g.MapGet("/organizations", async (TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Organizations.AsNoTracking().OrderBy(o => o.Name)
                    .Select(o => new OrganizationDto(o.Id, o.Name)).ToListAsync(ct)))
            .RequirePermission("core.tenancy.structure.read");

        g.MapPost("/organizations", async (CreateOrganizationRequest req, TenancyDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required (max 200 characters)."] });
                }

                var org = new Organization { Id = Guid.CreateVersion7(), TenantId = ctx.TenantId!.Value, Name = req.Name.Trim(), CreatedAt = clock.GetUtcNow() };
                db.Organizations.Add(org);
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.tenancy.organization.created", "organization", org.Id.ToString()), ct);
                return Results.Created($"/api/core/tenancy/organizations/{org.Id}", new OrganizationDto(org.Id, org.Name));
            })
            .RequirePermission("core.tenancy.structure.manage");

        g.MapGet("/sites", async (TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Sites.AsNoTracking().OrderBy(s => s.Code)
                    .Select(s => new SiteDto(s.Id, s.OrganizationId, s.Code, s.Name, s.TimeZone)).ToListAsync(ct)))
            .RequirePermission("core.tenancy.structure.read");

        g.MapPost("/sites", async (CreateSiteRequest req, TenancyDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(req.Code) || !SiteCode().IsMatch(req.Code))
                {
                    errors["code"] = ["Use 1-32 uppercase letters, digits or hyphens."];
                }

                if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
                {
                    errors["name"] = ["Name is required (max 200 characters)."];
                }

                if (string.IsNullOrWhiteSpace(req.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(req.TimeZone, out _))
                {
                    errors["timeZone"] = ["Use an IANA time zone such as 'Asia/Kolkata' or 'Europe/Berlin'."];
                }

                // Row-level security hides other tenants' organizations, so this also rejects cross-tenant ids.
                if (!await db.Organizations.AnyAsync(o => o.Id == req.OrganizationId, ct))
                {
                    errors["organizationId"] = ["Organization not found."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var site = new Site
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = ctx.TenantId!.Value,
                    OrganizationId = req.OrganizationId,
                    Code = req.Code,
                    Name = req.Name.Trim(),
                    TimeZone = req.TimeZone,
                    CreatedAt = clock.GetUtcNow(),
                };
                db.Sites.Add(site);
                try
                {
                    await db.SaveChangesWithAuditAsync(new AuditRecord("core.tenancy.site.created", "site", site.Id.ToString(),
                        Details: new Dictionary<string, object?> { ["code"] = site.Code }), ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Results.Conflict(new { title = $"Site code '{req.Code}' is already in use." });
                }

                return Results.Created($"/api/core/tenancy/sites/{site.Id}", new SiteDto(site.Id, site.OrganizationId, site.Code, site.Name, site.TimeZone));
            })
            .RequirePermission("core.tenancy.structure.manage");

        g.MapGet("/sites/{siteId:guid}/departments", async (Guid siteId, TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Departments.AsNoTracking().Where(d => d.SiteId == siteId).OrderBy(d => d.Name)
                    .Select(d => new DepartmentDto(d.Id, d.SiteId, d.Name)).ToListAsync(ct)))
            .RequirePermission("core.tenancy.structure.read");

        g.MapPost("/sites/{siteId:guid}/departments", async (Guid siteId, CreateDepartmentRequest req, TenancyDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required (max 200 characters)."] });
                }

                if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct))
                {
                    return Results.NotFound();
                }

                var dept = new Department { Id = Guid.CreateVersion7(), TenantId = ctx.TenantId!.Value, SiteId = siteId, Name = req.Name.Trim(), CreatedAt = clock.GetUtcNow() };
                db.Departments.Add(dept);
                try
                {
                    await db.SaveChangesWithAuditAsync(new AuditRecord("core.tenancy.department.created", "department", dept.Id.ToString()), ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Results.Conflict(new { title = $"Department '{req.Name}' already exists at this site." });
                }

                return Results.Created($"/api/core/tenancy/sites/{siteId}/departments/{dept.Id}", new DepartmentDto(dept.Id, dept.SiteId, dept.Name));
            })
            .RequirePermission("core.tenancy.structure.manage");
    }
}
