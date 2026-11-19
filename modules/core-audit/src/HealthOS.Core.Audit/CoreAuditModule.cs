using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Core.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid ChainKey { get; set; }
    public Guid? TenantId { get; set; }
    public long Seq { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public required string Outcome { get; set; }
    public required string Details { get; set; }
    public required byte[] PrevHash { get; set; }
    public required byte[] Hash { get; set; }
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_audit";

    protected override string Schema => SchemaName;

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_event");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChainKey).HasColumnName("chain_key");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.ResourceType).HasColumnName("resource_type");
            e.Property(x => x.ResourceId).HasColumnName("resource_id");
            e.Property(x => x.Outcome).HasColumnName("outcome");
            e.Property(x => x.Details).HasColumnName("details").HasColumnType("jsonb");
            e.Property(x => x.PrevHash).HasColumnName("prev_hash");
            e.Property(x => x.Hash).HasColumnName("hash");
        });
    }
}

/// <summary>Standalone audit writer for events with no surrounding business transaction (e.g. denials).</summary>
public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

internal sealed class AuditLog(AuditDbContext db) : IAuditLog
{
    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) => db.AppendAuditAsync(record, cancellationToken);
}

public sealed record AuditEventDto(
    long Seq, DateTimeOffset OccurredAt, Guid? ActorUserId, string Action, string ResourceType, string? ResourceId,
    string Outcome, string Details, string Hash);

public sealed record AuditVerificationDto(bool Intact, long? FirstInvalidSeq);

public sealed class CoreAuditModule : HealthOsModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<AuditDbContext>(Manifest.Id, AuditDbContext.SchemaName);
        services.AddScoped<IAuditLog, AuditLog>();
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Same handlers for both chains: row-level security decides which chain is visible.
        Map(endpoints.MapGroup("/api/core/audit").WithTags("core.audit"), "core.audit.events.read");
        Map(endpoints.MapGroup("/api/platform/audit").WithTags("platform.audit"), "core.audit.platform-events.read");
    }

    private static void Map(RouteGroupBuilder g, string permission)
    {
        g.MapGet("/events", async (AuditDbContext db, int? limit, long? beforeSeq, CancellationToken ct) =>
            {
                var take = Math.Clamp(limit ?? 100, 1, 500);
                var query = db.Events.AsNoTracking();
                if (beforeSeq is { } b)
                {
                    query = query.Where(e => e.Seq < b);
                }

                var rows = await query.OrderByDescending(e => e.Seq).Take(take).ToListAsync(ct);
                return TypedResults.Ok(rows.Select(e => new AuditEventDto(
                    e.Seq, e.OccurredAt, e.ActorUserId, e.Action, e.ResourceType, e.ResourceId, e.Outcome, e.Details, Convert.ToHexStringLower(e.Hash))));
            })
            .RequirePermission(permission);

        g.MapGet("/verify", async (AuditDbContext db, CancellationToken ct) =>
            {
                var first = await db.Database.SqlQuery<long?>($"SELECT core_audit.verify() AS \"Value\"").SingleAsync(ct);
                return TypedResults.Ok(new AuditVerificationDto(first is null, first));
            })
            .RequirePermission(permission);
    }
}
