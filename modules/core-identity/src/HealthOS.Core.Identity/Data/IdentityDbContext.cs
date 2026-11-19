using HealthOS.Modules.Sdk.Compliance;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core_identity";

    protected override string Schema => SchemaName;

    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("user_account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Issuer).HasColumnName("issuer").HasMaxLength(512).IsRequired();
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(255).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastSignInAt).HasColumnName("last_sign_in_at");
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique().HasDatabaseName("ux_user_account_issuer_subject");
        });
    }
}

/// <summary>Used only by `dotnet ef` at design time to generate migrations; never connects.</summary>
public sealed class IdentityDbContextDesignFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=design-time-only", n => n.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
            .Options;
        return new IdentityDbContext(o);
    }
}
