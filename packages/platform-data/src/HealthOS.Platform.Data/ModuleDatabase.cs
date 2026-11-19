using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static HealthOS.Platform.Data.DatabaseRoles;

namespace HealthOS.Platform.Data;

/// <summary>Base class for a module's EF Core context. All tables live in the module's own schema.</summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Switches every opened connection to the module's role. The login role (hos_app) is NOINHERIT with
/// no grants, so a context without this interceptor cannot read or write anything (fail closed).
/// Npgsql resets session state (DISCARD ALL) when a connection returns to the pool.
/// </summary>
public sealed class ModuleRoleInterceptor(string moduleRole) : DbConnectionInterceptor
{
    private readonly string _sql = $"SET ROLE {Q(moduleRole)}";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _sql;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = _sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PlatformDatabaseOptions
{
    /// <summary>Runtime connection as the application login role.</summary>
    public required string AppConnectionString { get; init; }

    public DatabaseRoles Roles { get; init; } = new();
}

/// <summary>Applies one module's schema setup and migrations. Registered by each module that owns tables.</summary>
public interface IModuleMigrations
{
    string ModuleId { get; }

    string Schema { get; }

    Task MigrateAsync(string migratorConnectionString, CancellationToken ct);
}

public static class ModuleDatabaseServiceCollectionExtensions
{
    /// <summary>Registers a module's DbContext (runtime: app login + SET ROLE) and its migrations.</summary>
    public static IServiceCollection AddModuleDatabase<TContext>(
        this IServiceCollection services,
        string moduleId,
        string schema,
        Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, o) =>
        {
            var platform = sp.GetRequiredService<PlatformDatabaseOptions>();
            ConfigureNpgsql(o, platform.AppConnectionString, schema);
            o.AddInterceptors(new ModuleRoleInterceptor(platform.Roles.ForModuleSchema(schema)));
        });

        services.AddSingleton<IModuleMigrations>(new EfModuleMigrations<TContext>(moduleId, schema, factory));
        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder o, string connectionString, string schema) =>
        o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", schema));
}

internal sealed class EfModuleMigrations<TContext>(string moduleId, string schema, Func<DbContextOptions<TContext>, TContext> factory)
    : IModuleMigrations
    where TContext : ModuleDbContext
{
    public string ModuleId { get; } = moduleId;

    public string Schema { get; } = schema;

    public async Task MigrateAsync(string migratorConnectionString, CancellationToken ct)
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        ModuleDatabaseServiceCollectionExtensions.ConfigureNpgsql(builder, migratorConnectionString, Schema);
        await using var ctx = factory(builder.Options);
        await ctx.Database.MigrateAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Runs as the migrator role: ensures each module's schema and module role, wires default privileges
/// so the module role can use tables the migrator creates, then applies EF migrations. Idempotent.
/// </summary>
public static class ModuleMigrator
{
    public static async Task MigrateAllAsync(
        string migratorConnectionString,
        DatabaseRoles roles,
        IEnumerable<IModuleMigrations> modules,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            await PrepareSchemaAsync(migratorConnectionString, roles, module.Schema, ct).ConfigureAwait(false);
            await module.MigrateAsync(migratorConnectionString, ct).ConfigureAwait(false);
            await GrantExistingObjectsAsync(migratorConnectionString, roles, module.Schema, ct).ConfigureAwait(false);
        }
    }

    internal static async Task PrepareSchemaAsync(string migratorConnectionString, DatabaseRoles roles, string schema, CancellationToken ct)
    {
        var moduleRole = roles.ForModuleSchema(schema);
        await using var c = new NpgsqlConnection(migratorConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);

        if (!await DatabaseBootstrapper.ExistsAsync(c, "SELECT 1 FROM pg_roles WHERE rolname = @n", moduleRole, ct).ConfigureAwait(false))
        {
            await DatabaseBootstrapper.ExecAsync(c, $"CREATE ROLE {Q(moduleRole)} NOLOGIN NOINHERIT NOBYPASSRLS", ct).ConfigureAwait(false);
        }

        // The application login may switch to (but does not inherit) the module role.
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT {Q(moduleRole)} TO {Q(roles.App)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"CREATE SCHEMA IF NOT EXISTS {Q(schema)} AUTHORIZATION {Q(roles.Migrator)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"REVOKE ALL ON SCHEMA {Q(schema)} FROM PUBLIC", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT USAGE ON SCHEMA {Q(schema)} TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"ALTER DEFAULT PRIVILEGES FOR ROLE {Q(roles.Migrator)} IN SCHEMA {Q(schema)} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"ALTER DEFAULT PRIVILEGES FOR ROLE {Q(roles.Migrator)} IN SCHEMA {Q(schema)} GRANT USAGE, SELECT ON SEQUENCES TO {Q(moduleRole)}", ct).ConfigureAwait(false);
    }

    private static async Task GrantExistingObjectsAsync(string migratorConnectionString, DatabaseRoles roles, string schema, CancellationToken ct)
    {
        var moduleRole = roles.ForModuleSchema(schema);
        await using var c = new NpgsqlConnection(migratorConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Q(schema)} TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Q(schema)} TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        // The migration history table is internal to the migrator.
        await DatabaseBootstrapper.ExecAsync(c, $"REVOKE ALL ON TABLE {Q(schema)}.\"__ef_migrations_history\" FROM {Q(moduleRole)}", ct).ConfigureAwait(false);
    }
}
