using System.Reflection;
using System.Text.Json;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthOS.Core.Configuration;

public sealed class TenantProfile
{
    public Guid TenantId { get; set; }
    public required string Country { get; set; }
    public required string Industry { get; set; }
    public string? Specialty { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public static class DraftStatus
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Published = "published";
    public const string Discarded = "discarded";
}

public sealed class ConfigDraft
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int BaseVersion { get; set; }
    public required string Summary { get; set; }
    public required string Entries { get; set; }
    public string Status { get; set; } = DraftStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
}

public sealed class ConfigSnapshot
{
    public Guid TenantId { get; set; }
    public int Version { get; set; }
    public required string Entries { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? DraftId { get; set; }
    public int? RolledBackTo { get; set; }
}

public sealed class DefinitionDocument
{
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }
    public required string Code { get; set; }
    public int Version { get; set; }
    public required string Document { get; set; }
    public string Source { get; set; } = "tenant";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : ModuleDbContext(options)
{
    public DbSet<DefinitionDocument> Definitions => Set<DefinitionDocument>();

    public const string SchemaName = "core_configuration";

    protected override string Schema => SchemaName;

    public DbSet<TenantProfile> Profiles => Set<TenantProfile>();
    public DbSet<ConfigDraft> Drafts => Set<ConfigDraft>();
    public DbSet<ConfigSnapshot> Snapshots => Set<ConfigSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TenantProfile>(e =>
        {
            e.ToTable("tenant_profile");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Country).HasColumnName("country");
            e.Property(x => x.Industry).HasColumnName("industry");
            e.Property(x => x.Specialty).HasColumnName("specialty");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        });
        modelBuilder.Entity<ConfigDraft>(e =>
        {
            e.ToTable("config_draft");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.BaseVersion).HasColumnName("base_version");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Entries).HasColumnName("entries").HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
        });
        modelBuilder.Entity<DefinitionDocument>(e =>
        {
            e.ToTable("definition");
            e.HasKey(x => new { x.TenantId, x.Kind, x.Code, x.Version });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Document).HasColumnName("document").HasColumnType("jsonb");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
        });
        modelBuilder.Entity<ConfigSnapshot>(e =>
        {
            e.ToTable("config_snapshot");
            e.HasKey(x => new { x.TenantId, x.Version });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Entries).HasColumnName("entries").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.DraftId).HasColumnName("draft_id");
            e.Property(x => x.RolledBackTo).HasColumnName("rolled_back_to");
        });
    }
}

public sealed record ProfileEntry(string Key, JsonElement? Value, bool Locked = false, decimal? Min = null, decimal? Max = null);

public sealed record ConfigProfile(ConfigScope Scope, string Key, string DisplayName, string Note, IReadOnlyList<ProfileEntry> Entries)
{
    public IEnumerable<SettingEntry> ToEntries() => Entries.Select(e => new SettingEntry
    {
        Key = e.Key, Scope = Scope, ScopeKey = Key, Value = e.Value, Locked = e.Locked, Min = e.Min, Max = e.Max,
    });
}

/// <summary>Country / industry / specialty profiles shipped with the platform (Profiles/*.json).</summary>
public sealed class ProfileCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ProfileCatalog()
    {
        var asm = Assembly.GetExecutingAssembly();
        All = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Profiles.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n =>
            {
                using var s = asm.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<ConfigProfile>(s, Json)!;
            })
            .OrderBy(p => p.Scope).ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<ConfigProfile> All { get; }

    public ConfigProfile? Find(ConfigScope scope, string? key) =>
        key is null ? null : All.FirstOrDefault(p => p.Scope == scope && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entries of the tenant's country, industry and specialty profiles.</summary>
    public IEnumerable<SettingEntry> EntriesFor(TenantProfile? profile) =>
        profile is null
            ? []
            : new[] { Find(ConfigScope.Country, profile.Country), Find(ConfigScope.Industry, profile.Industry), Find(ConfigScope.Specialty, profile.Specialty) }
                .Where(p => p is not null).SelectMany(p => p!.ToEntries());
}
