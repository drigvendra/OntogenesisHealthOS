using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Configuration;

/// <summary>
/// Configuration Studio API. Lifecycle: draft → (validate) → preview → approve → publish → (rollback).
/// Published snapshots are immutable; rollback publishes a copy of an older snapshot. AI copilots may
/// only create drafts (M4). Every lifecycle step is audited.
/// </summary>
internal static class ConfigurationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/core/configuration").WithTags("core.configuration");

        g.MapGet("/definitions", (SettingsCatalog catalog) => TypedResults.Ok(catalog.All))
            .RequirePermission("core.configuration.settings.read");

        g.MapGet("/profile", async (ConfigurationService config, IRequestContext ctx, CancellationToken ct) =>
                await config.ProfileAsync(ctx.TenantId!.Value, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequirePermission("core.configuration.settings.read");

        g.MapGet("/current", async (ConfigurationService config, IRequestContext ctx, CancellationToken ct) =>
            {
                var s = await config.CurrentAsync(ctx.TenantId!.Value, ct);
                return TypedResults.Ok(new { version = s?.Version ?? 0, entries = s is null ? [] : ConfigurationService.Parse(s.Entries) });
            })
            .RequirePermission("core.configuration.settings.read");

        g.MapGet("/snapshots", async (ConfigurationDbContext db, CancellationToken ct) =>
                TypedResults.Ok((await db.Snapshots.AsNoTracking().OrderByDescending(s => s.Version).ToListAsync(ct))
                    .Select(s => new SnapshotDto(s.Version, s.CreatedAt, s.CreatedBy, s.DraftId, s.RolledBackTo, ConfigurationService.Parse(s.Entries).Count))))
            .RequirePermission("core.configuration.settings.read");

        g.MapGet("/drafts", async (ConfigurationDbContext db, CancellationToken ct) =>
                TypedResults.Ok((await db.Drafts.AsNoTracking().OrderByDescending(d => d.CreatedAt).Take(50).ToListAsync(ct)).Select(ToDto)))
            .RequirePermission("core.configuration.settings.read");

        g.MapPost("/drafts", async (CreateDraftRequest req, ConfigurationService config, ConfigurationDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var tenantId = ctx.TenantId!.Value;
                if (string.IsNullOrWhiteSpace(req.Summary) || req.Summary.Length > 500)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["summary"] = ["Describe the change (max 500 characters)."] });
                }

                var problems = config.Validate(req.Entries ?? [], await config.ProfileAsync(tenantId, ct));
                if (problems.Count > 0)
                {
                    return Results.BadRequest(new { title = "The draft has invalid entries.", problems });
                }

                var draft = new ConfigDraft
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    BaseVersion = (await config.CurrentAsync(tenantId, ct))?.Version ?? 0,
                    Summary = req.Summary.Trim(),
                    Entries = ConfigurationService.Serialize(req.Entries ?? []),
                    CreatedAt = clock.GetUtcNow(),
                    CreatedBy = ctx.UserId!.Value,
                };
                db.Drafts.Add(draft);
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.draft.created", "config-draft", draft.Id.ToString(),
                    Details: new Dictionary<string, object?> { ["baseVersion"] = draft.BaseVersion, ["entries"] = req.Entries?.Count ?? 0 }), ct);
                return Results.Created($"/api/core/configuration/drafts/{draft.Id}", ToDto(draft));
            })
            .RequirePermission("core.configuration.drafts.edit");

        g.MapGet("/drafts/{id:guid}", async (Guid id, ConfigurationDbContext db, CancellationToken ct) =>
                await db.Drafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct) is { } d ? Results.Ok(ToDto(d)) : Results.NotFound())
            .RequirePermission("core.configuration.settings.read");

        // Preview: effective values for a hypothetical user/site under the draft, with full trace.
        g.MapPost("/drafts/{id:guid}/preview", async (Guid id, PreviewRequest req, ConfigurationService config, ConfigurationDbContext db, IRequestContext ctx, CancellationToken ct) =>
            {
                if (await db.Drafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct) is not { } draft)
                {
                    return Results.NotFound();
                }

                var profile = await config.ProfileAsync(ctx.TenantId!.Value, ct);
                var context = config.Context(profile, req.OrganizationId, req.SiteId, req.Roles ?? [], req.UserId);
                return Results.Ok(config.ResolveAll(ConfigurationService.Parse(draft.Entries), profile, context));
            })
            .RequirePermission("core.configuration.settings.read");

        g.MapPost("/drafts/{id:guid}/approve", async (Guid id, ConfigurationService config, ConfigurationDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var tenantId = ctx.TenantId!.Value;
                if (await db.Drafts.SingleOrDefaultAsync(d => d.Id == id, ct) is not { } draft)
                {
                    return Results.NotFound();
                }

                if (draft.Status != DraftStatus.Draft)
                {
                    return Results.Conflict(new { title = $"Draft is {draft.Status}." });
                }

                if (draft.CreatedBy == ctx.UserId && await config.FourEyesAsync(tenantId, ct))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Four-eyes rule: another person must approve this draft.");
                }

                // Re-validate against the current profile (a profile change may have added locks).
                var problems = config.Validate(ConfigurationService.Parse(draft.Entries), await config.ProfileAsync(tenantId, ct));
                if (problems.Count > 0)
                {
                    return Results.BadRequest(new { title = "The draft is no longer valid.", problems });
                }

                draft.Status = DraftStatus.Approved;
                draft.ApprovedBy = ctx.UserId;
                draft.ApprovedAt = clock.GetUtcNow();
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.draft.approved", "config-draft", draft.Id.ToString()), ct);
                return Results.Ok(ToDto(draft));
            })
            .RequirePermission("core.configuration.drafts.approve");

        g.MapPost("/drafts/{id:guid}/publish", async (Guid id, ConfigurationService config, ConfigurationDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var tenantId = ctx.TenantId!.Value;
                if (await db.Drafts.SingleOrDefaultAsync(d => d.Id == id, ct) is not { } draft)
                {
                    return Results.NotFound();
                }

                if (draft.Status != DraftStatus.Approved)
                {
                    return Results.Conflict(new { title = "Only approved drafts can be published." });
                }

                var current = (await config.CurrentAsync(tenantId, ct))?.Version ?? 0;
                if (draft.BaseVersion != current)
                {
                    return Results.Conflict(new { title = $"Configuration changed since this draft was created (draft based on v{draft.BaseVersion}, current v{current}). Create a new draft." });
                }

                var snapshot = new ConfigSnapshot
                {
                    TenantId = tenantId, Version = current + 1, Entries = draft.Entries, CreatedAt = clock.GetUtcNow(), CreatedBy = ctx.UserId!.Value, DraftId = draft.Id,
                };
                db.Snapshots.Add(snapshot);
                draft.Status = DraftStatus.Published;
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.snapshot.published", "config-snapshot", snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Details: new Dictionary<string, object?> { ["draftId"] = draft.Id, ["approvedBy"] = draft.ApprovedBy }), ct);
                return Results.Ok(new SnapshotDto(snapshot.Version, snapshot.CreatedAt, snapshot.CreatedBy, snapshot.DraftId, null, ConfigurationService.Parse(snapshot.Entries).Count));
            })
            .RequirePermission("core.configuration.snapshots.publish");

        g.MapPost("/rollback", async (RollbackRequest req, ConfigurationService config, ConfigurationDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var tenantId = ctx.TenantId!.Value;
                var target = await db.Snapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Version == req.ToVersion, ct);
                if (target is null && req.ToVersion != 0)
                {
                    return Results.NotFound();
                }

                var current = (await config.CurrentAsync(tenantId, ct))?.Version ?? 0;
                var snapshot = new ConfigSnapshot
                {
                    TenantId = tenantId, Version = current + 1, Entries = target?.Entries ?? "[]", CreatedAt = clock.GetUtcNow(), CreatedBy = ctx.UserId!.Value, RolledBackTo = req.ToVersion,
                };
                db.Snapshots.Add(snapshot);
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.snapshot.rolled-back", "config-snapshot", snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Details: new Dictionary<string, object?> { ["toVersion"] = req.ToVersion }), ct);
                return Results.Ok(new SnapshotDto(snapshot.Version, snapshot.CreatedAt, snapshot.CreatedBy, null, snapshot.RolledBackTo, ConfigurationService.Parse(snapshot.Entries).Count));
            })
            .RequirePermission("core.configuration.snapshots.publish");

        // Any member may see their own effective settings (with provenance).
        g.MapGet("/effective", async (ISettingsReader reader, SettingsCatalog catalog, IRequestContext ctx, string? siteId, CancellationToken ct) =>
            {
                if (ctx.TenantId is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Choose a tenant first.");
                }

                var results = new List<ResolvedSetting>();
                foreach (var d in catalog.All)
                {
                    results.Add(await reader.GetAsync(d.Key, siteId, ct));
                }

                return Results.Ok(results);
            })
            .RequireAuthenticatedUser("Members may read the settings that apply to themselves.");

        // Platform: profiles and tenant profile assignment (onboarding).
        var p = endpoints.MapGroup("/api/platform").WithTags("platform.configuration");
        p.MapGet("/configuration/profiles", (ProfileCatalog profiles) => TypedResults.Ok(profiles.All))
            .RequirePermission("core.configuration.profile.manage");
        p.MapPut("/tenants/{tenantId:guid}/configuration/profile", async (Guid tenantId, SetProfileRequest req, ProfileCatalog profiles,
                ConfigurationDbContext db, IRequestContext ctx, TimeProvider clock, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (profiles.Find(ConfigScope.Country, req.Country) is null)
                {
                    errors["country"] = ["Unknown country profile."];
                }

                if (profiles.Find(ConfigScope.Industry, req.Industry) is null)
                {
                    errors["industry"] = ["Unknown industry profile."];
                }

                if (req.Specialty is not null && profiles.Find(ConfigScope.Specialty, req.Specialty) is null)
                {
                    errors["specialty"] = ["Unknown specialty profile."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var profile = await db.Profiles.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
                if (profile is null)
                {
                    profile = new TenantProfile { TenantId = tenantId, Country = req.Country, Industry = req.Industry };
                    db.Profiles.Add(profile);
                }

                profile.Country = profiles.Find(ConfigScope.Country, req.Country)!.Key;
                profile.Industry = profiles.Find(ConfigScope.Industry, req.Industry)!.Key;
                profile.Specialty = req.Specialty is null ? null : profiles.Find(ConfigScope.Specialty, req.Specialty)!.Key;
                profile.UpdatedAt = clock.GetUtcNow();
                profile.UpdatedBy = ctx.UserId;
                await db.SaveChangesWithAuditAsync(new AuditRecord("core.configuration.profile.set", "tenant", tenantId.ToString(),
                    Details: new Dictionary<string, object?> { ["country"] = profile.Country, ["industry"] = profile.Industry, ["specialty"] = profile.Specialty }), ct);
                return Results.Ok(profile);
            })
            .RequirePermission("core.configuration.profile.manage");
    }

    private static DraftDto ToDto(ConfigDraft d) =>
        new(d.Id, d.BaseVersion, d.Summary, d.Status, ConfigurationService.Parse(d.Entries), d.CreatedAt, d.CreatedBy, d.ApprovedBy, d.ApprovedAt);
}
