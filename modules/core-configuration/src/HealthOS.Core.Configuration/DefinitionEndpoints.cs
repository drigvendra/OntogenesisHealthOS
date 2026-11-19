using System.Text.Json;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Configuration;

public sealed record DefinitionSummary(string Code, string Title, int Version, string Source, DateTimeOffset CreatedAt);

public sealed record ValidateValuesRequest(Dictionary<string, JsonElement> Values);

/// <summary>Stores and versions form and workflow definitions for a tenant.</summary>
public sealed class DefinitionStore(ConfigurationDbContext db, PermissionCatalog permissions, TimeProvider clock)
{
    public const string Form = "form";
    public const string Workflow = "workflow";

    public IReadOnlyList<string> Validate(string kind, JsonElement document)
    {
        try
        {
            return kind == Form
                ? FormEngine.ValidateDefinition(document.Deserialize<FormDefinition>(ConfigurationService.Json)!)
                : WorkflowValidator.Validate(document.Deserialize<WorkflowDefinition>(ConfigurationService.Json)!, p => permissions.Find(p) is not null);
        }
        catch (JsonException ex)
        {
            return [$"not a valid {kind} definition: {ex.Message}"];
        }
    }

    /// <summary>Publishes a new version (validated). Returns problems instead when invalid.</summary>
    public async Task<(DefinitionDocument? Saved, IReadOnlyList<string> Problems)> PublishAsync(
        Guid tenantId, string kind, JsonElement document, string source, Guid? userId, CancellationToken ct, bool skipIfUnchanged = false)
    {
        var problems = Validate(kind, document);
        if (problems.Count > 0)
        {
            return (null, problems);
        }

        var code = document.GetProperty("code").GetString()!;
        var current = await GetAsync(tenantId, kind, code, null, ct).ConfigureAwait(false);
        if (skipIfUnchanged && current is not null && Canonical(JsonDocument.Parse(current.Document).RootElement) == Canonical(document))
        {
            return (current, []);
        }

        var latest = current?.Version ?? 0;
        var saved = new DefinitionDocument
        {
            TenantId = tenantId, Kind = kind, Code = code, Version = latest + 1, Document = document.GetRawText(), Source = source,
            CreatedAt = clock.GetUtcNow(), CreatedBy = userId,
        };
        db.Definitions.Add(saved);
        await db.SaveChangesWithAuditAsync(new AuditRecord($"core.configuration.{kind}.published", kind, code,
            Details: new Dictionary<string, object?> { ["version"] = saved.Version, ["source"] = source }), ct).ConfigureAwait(false);
        return (saved, []);
    }

    /// <summary>Key-order-independent JSON text (PostgreSQL jsonb does not preserve key order).</summary>
    internal static string Canonical(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.Number => e.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => e.GetRawText(),
    };

    public Task<DefinitionDocument?> GetAsync(Guid tenantId, string kind, string code, int? version, CancellationToken ct) =>
        db.Definitions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Kind == kind && d.Code == code && (version == null || d.Version == version))
            .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<DefinitionSummary>> LatestAsync(Guid tenantId, string kind, CancellationToken ct)
    {
        var rows = await db.Definitions.AsNoTracking().Where(d => d.TenantId == tenantId && d.Kind == kind).ToListAsync(ct).ConfigureAwait(false);
        return rows.GroupBy(d => d.Code).Select(g => g.OrderByDescending(d => d.Version).First())
            .Select(d => new DefinitionSummary(d.Code, JsonDocument.Parse(d.Document).RootElement.GetProperty("title").GetString() ?? d.Code, d.Version, d.Source, d.CreatedAt))
            .OrderBy(d => d.Code, StringComparer.Ordinal).ToList();
    }
}

internal static class DefinitionEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        foreach (var (kind, path) in new[] { (DefinitionStore.Form, "forms"), (DefinitionStore.Workflow, "workflows") })
        {
            var g = endpoints.MapGroup($"/api/core/configuration/{path}").WithTags("core.configuration");

            g.MapGet("/", async (DefinitionStore store, IRequestContext ctx, CancellationToken ct) =>
                    TypedResults.Ok(await store.LatestAsync(ctx.TenantId!.Value, kind, ct)))
                .RequirePermission("core.configuration.settings.read");

            g.MapGet("/{code}", async (string code, int? version, DefinitionStore store, IRequestContext ctx, CancellationToken ct) =>
                    await store.GetAsync(ctx.TenantId!.Value, kind, code, version, ct) is { } d
                        ? Results.Text(d.Document, "application/json")
                        : Results.NotFound())
                .RequirePermission("core.configuration.settings.read");

            g.MapPost("/", async (JsonElement document, DefinitionStore store, IRequestContext ctx, CancellationToken ct) =>
                {
                    var (saved, problems) = await store.PublishAsync(ctx.TenantId!.Value, kind, document, "tenant", ctx.UserId, ct);
                    return saved is null
                        ? Results.BadRequest(new { title = $"The {kind} definition is invalid.", problems })
                        : Results.Created($"/api/core/configuration/{path}/{saved.Code}?version={saved.Version}", new { saved.Code, saved.Version });
                })
                .RequirePermission("core.configuration.snapshots.publish");
        }

        // Check submitted values against a published form (used by form designers' preview and by modules).
        endpoints.MapPost("/api/core/configuration/forms/{code}/validate-values", async (string code, ValidateValuesRequest req, DefinitionStore store, IRequestContext ctx, CancellationToken ct) =>
            {
                if (await store.GetAsync(ctx.TenantId!.Value, DefinitionStore.Form, code, null, ct) is not { } d)
                {
                    return Results.NotFound();
                }

                var form = JsonSerializer.Deserialize<FormDefinition>(d.Document, ConfigurationService.Json)!;
                return Results.Ok(new { version = d.Version, errors = FormEngine.ValidateValues(form, req.Values ?? []) });
            })
            .RequirePermission("core.configuration.settings.read");
    }
}
