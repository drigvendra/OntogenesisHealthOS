using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Platform.Data;

/// <summary>
/// An audit entry. Details must contain identifiers and codes only — never PHI or free text entered by
/// users. Tenant and actor are taken from the database session context, not from this record.
/// </summary>
public sealed record AuditRecord(
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome = AuditOutcome.Success,
    IReadOnlyDictionary<string, object?>? Details = null);

public static class AuditOutcome
{
    public const string Success = "success";
    public const string Denied = "denied";
    public const string Failure = "failure";
}

/// <summary>
/// Appends to the core.audit hash chain through the module's own connection, so the audit entry and
/// the business change commit or roll back together. Requires the core.audit module (declare it as a
/// dependency in module.json).
/// </summary>
public static class AuditTrailExtensions
{
    public static async Task AppendAuditAsync(this DbContext db, AuditRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(record);
        var details = JsonSerializer.Serialize(record.Details ?? new Dictionary<string, object?>());
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT core_audit.append({record.Action}, {record.ResourceType}, {record.ResourceId}, {record.Outcome}, CAST({details} AS jsonb))",
            ct).ConfigureAwait(false);
    }

    /// <summary>Saves pending changes and appends the audit entry in one transaction.</summary>
    public static async Task<int> SaveChangesWithAuditAsync(this DbContext db, AuditRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is not null)
        {
            var n = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await db.AppendAuditAsync(record, ct).ConfigureAwait(false);
            return n;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var n = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await db.AppendAuditAsync(record, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return n;
        }).ConfigureAwait(false);
    }
}
