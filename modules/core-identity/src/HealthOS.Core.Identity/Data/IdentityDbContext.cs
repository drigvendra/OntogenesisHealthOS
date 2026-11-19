using HealthOS.Modules.Sdk.Compliance;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Identity.Data;

public sealed class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>OIDC issuer URL; subjects are only unique per issuer.</summary>
    public required string Issuer { get; set; }

    public required string Subject { get; set; }

    [Pii]
    public string? DisplayName { get; set; }

    [Pii]
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignInAt { get; set; }
}

public sealed class PlatformOperator
{
    public Guid UserId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
}

public sealed class TenantMembership
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = MembershipStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class MembershipStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

public sealed class Role
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RolePermission
{
    public Guid TenantId { get; set; }
    public Guid RoleId { get; set; }
    public required string Permission { get; set; }
}

public sealed class RoleAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? SiteId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_identity";

    protected override string Schema => SchemaName;

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<PlatformOperator> PlatformOperators => Set<PlatformOperator>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("user_account");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Issuer).HasColumnName("issuer");
            e.Property(x => x.Subject).HasColumnName("subject");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastSignInAt).HasColumnName("last_sign_in_at");
        });

        modelBuilder.Entity<PlatformOperator>(e =>
        {
            e.ToTable("platform_operator");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.GrantedAt).HasColumnName("granted_at");
            e.Property(x => x.GrantedBy).HasColumnName("granted_by");
        });

        modelBuilder.Entity<TenantMembership>(e =>
        {
            e.ToTable("tenant_membership");
            e.HasKey(x => new { x.TenantId, x.UserId });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("role");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.IsSystem).HasColumnName("is_system");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permission");
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.Permission).HasColumnName("permission");
        });

        modelBuilder.Entity<RoleAssignment>(e =>
        {
            e.ToTable("role_assignment");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.SiteId).HasColumnName("site_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
