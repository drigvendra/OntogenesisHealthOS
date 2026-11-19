using System.Data.Common;
using HealthOS.Modules.Sdk.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static HealthOS.Platform.Data.DatabaseRoles;

namespace HealthOS.Platform.Data;

/// <summary>Base class for a module's EF Core context. All tables live in the module's own schema.
/// Schema changes are made by SQL migrations (see <see cref="SqlModuleMigrations"/>), not by EF.</summary>
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
/// On every opened connection: switch to the module's role and copy the request context into the
/// PostgreSQL session (app.tenant_id, app.user_id, app.platform), where row-level security policies
/// read it. The login role (hos_app) has no privileges of its own, so a context without this
/// interceptor can read nothing. Npgsql resets session state when a connection returns to the pool.
/// </summary>
public sealed class ModuleSessionInterceptor(string moduleRole, IRequestContext context) : DbConnectionInterceptor
{
    private readonly string _setRole = $"SET ROLE {Q(moduleRole)}";

    private const string SetContext =
        "SELECT set_config('app.tenant_id', @t, false), set_config('app.user_id', @u, false), set_config('app.platform', @p, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using (var role = connection.CreateCommand())
        {
            role.CommandText = _setRole;
            role.ExecuteNonQuery();
        }

        using var ctx = BuildContextCommand(connection);
        ctx.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using (var role = connection.CreateCommand())
        {
            role.CommandText = _setRole;
            await role.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var ctx = BuildContextCommand(connection);
        await ctx.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand BuildContextCommand(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = SetContext;
        Add(cmd, "t", context.TenantId?.ToString() ?? string.Empty);
        Add(cmd, "u", context.UserId?.ToString() ?? string.Empty);
        Add(cmd, "p", context.IsPlatformScope ? "on" : "off");
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

public sealed class PlatformDatabaseOptions
{
    /// <summary>Runtime connection as the application login role.</summary>
    public required string AppConnectionString { get; init; }

    public DatabaseRoles Roles { get; init; } = new();
}

public static class ModuleDatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module's DbContext (runtime: app login + SET ROLE + request context) and its SQL
    /// migrations (embedded resources named "*.Migrations.NNNN_description.sql" in the module assembly).
    /// </summary>
    public static IServiceCollection AddModuleDatabase<TContext>(this IServiceCollection services, string moduleId, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, o) =>
        {
            var platform = sp.GetRequiredService<PlatformDatabaseOptions>();
            var context = sp.GetService<IRequestContext>() ?? new RequestContext();
            o.UseNpgsql(platform.AppConnectionString);
            o.AddInterceptors(new ModuleSessionInterceptor(platform.Roles.ForModuleSchema(schema), context));
        });

        services.AddSingleton<IModuleMigrations>(new SqlModuleMigrations(moduleId, schema, typeof(TContext).Assembly));
        return services;
    }
}
