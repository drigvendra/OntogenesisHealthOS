using System.Security.Cryptography;
using HealthOS.Core.Identity;
using HealthOS.Core.Tenancy;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>
/// Creates an isolated database and role set per test run on the PostgreSQL server given by
/// HEALTHOS_TEST_PG_ADMIN (an administrative connection string), migrates all platform modules,
/// and drops everything afterwards. Without the variable, tests are reported as skipped, never passed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string AdminVariable = "HEALTHOS_TEST_PG_ADMIN";

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(AdminVariable);

    public DatabaseRoles Roles { get; } = new("hostest_" + RandomNumberGenerator.GetHexString(8, lowercase: true));

    public string DatabaseName => Roles.Prefix + "_db";

    public string MigratorPassword { get; } = RandomNumberGenerator.GetHexString(32);

    public string AppPassword { get; } = RandomNumberGenerator.GetHexString(32);

    public string MigratorConnectionString => As(Roles.Migrator, MigratorPassword);

    public string AppConnectionString => As(Roles.App, AppPassword);

    public string AdminDatabaseConnectionString =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = DatabaseName }.ConnectionString;

    public static IReadOnlyList<IHealthOsModule> Modules { get; } =
        [new HealthOS.Core.Audit.CoreAuditModule(), new CoreTenancyModule(), new HealthOS.Core.Licensing.CoreLicensingModule(), new CoreIdentityModule()];

    private string As(string user, string password) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = DatabaseName,
            Username = user,
            Password = password,
            Pooling = false,
        }.ConnectionString;

    public static void SkipIfUnavailable() =>
        Assert.SkipWhen(string.IsNullOrEmpty(AdminConnectionString), $"{AdminVariable} not set; database tests not executed.");

    /// <summary>Module services as the running app would have them, with a settable request context.</summary>
    public ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:Cell"] = "TEST-1" }).Build();
        services.AddSingleton(new PlatformDatabaseOptions { AppConnectionString = AppConnectionString, Roles = Roles });
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new PermissionCatalog(Modules.Select(m => m.Manifest)));
        foreach (var m in Modules)
        {
            m.ConfigureServices(services, config);
        }

        return services.BuildServiceProvider();
    }

    public async Task MigrateAsync()
    {
        await using var sp = BuildServices();
        await ModuleMigrator.MigrateAllAsync(MigratorConnectionString, Roles, sp.GetServices<IModuleMigrations>());
    }

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(AdminConnectionString))
        {
            return;
        }

        await DatabaseBootstrapper.BootstrapAsync(AdminConnectionString, DatabaseName, Roles, MigratorPassword, AppPassword);
        await MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(AdminConnectionString))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(AdminConnectionString);
        await c.OpenAsync();
        await Exec(c, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");

        await using var cmd = new NpgsqlCommand("SELECT rolname FROM pg_roles WHERE rolname LIKE @p ORDER BY length(rolname) DESC", c);
        cmd.Parameters.AddWithValue("p", Roles.Prefix + "\\_%");
        var roles = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                roles.Add(r.GetString(0));
            }
        }

        foreach (var role in roles)
        {
            await Exec(c, $"DROP ROLE IF EXISTS \"{role}\"");
        }
    }

    public static async Task Exec(NpgsqlConnection c, string sql)
    {
#pragma warning disable CA2100 // test-only SQL with generated identifiers
        await using var cmd = new NpgsqlCommand(sql, c);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<object?> Scalar(NpgsqlConnection c, string sql)
    {
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, c);
#pragma warning restore CA2100
        return await cmd.ExecuteScalarAsync();
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>;
