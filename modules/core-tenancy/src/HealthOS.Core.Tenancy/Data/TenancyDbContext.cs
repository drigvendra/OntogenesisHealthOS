using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Tenancy.Data;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string RegionCell { get; set; }
    public string Status { get; set; } = TenantStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public TenantSummary ToSummary() => new(Id, Code, Name, RegionCell, Status);
}

public sealed class Organization
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Site
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string TimeZone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Department
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_tenancy";

    protected override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenant");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.RegionCell).HasColumnName("region_cell");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<Organization>(e =>
        {
            e.ToTable("organization");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.ToTable("site");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.TimeZone).HasColumnName("time_zone");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // Relationships mirror the database foreign keys (tenant-scoped composite keys) so EF orders inserts.
        modelBuilder.Entity<Organization>().HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId);
        modelBuilder.Entity<Organization>().HasAlternateKey(o => new { o.TenantId, o.Id });
        modelBuilder.Entity<Site>().HasAlternateKey(s => new { s.TenantId, s.Id });
        modelBuilder.Entity<Site>().HasOne<Organization>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.OrganizationId }).HasPrincipalKey(o => new { o.TenantId, o.Id });
        modelBuilder.Entity<Department>().HasOne<Site>().WithMany()
            .HasForeignKey(d => new { d.TenantId, d.SiteId }).HasPrincipalKey(s => new { s.TenantId, s.Id });

        modelBuilder.Entity<Department>(e =>
        {
            e.ToTable("department");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.SiteId).HasColumnName("site_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}

internal sealed class TenantDirectory(TenancyDbContext db) : ITenantDirectory
{
    public async Task<TenantSummary?> FindAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var t = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken).ConfigureAwait(false);
        return t?.ToSummary();
    }

    public async Task<IReadOnlyList<TenantSummary>> GetManyAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        var list = await db.Tenants.AsNoTracking().Where(x => tenantIds.Contains(x.Id)).OrderBy(x => x.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.Select(t => t.ToSummary()).ToList();
    }
}
