using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>
/// M1-06 evidence: bootstrap and migrations are idempotent, and module data is isolated by
/// PostgreSQL privileges (not by application convention).
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class DatabaseIsolationTests(PostgresFixture db)
{
    private const string InsufficientPrivilege = "42501";

    private async Task MigrateAsync()
    {
        var modules = BuildServices().GetServices<IModuleMigrations>();
        await ModuleMigrator.MigrateAllAsync(db.MigratorConnectionString, db.Roles, modules, TestContext.Current.CancellationToken);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PlatformDatabaseOptions { AppConnectionString = db.AppConnectionString, Roles = db.Roles });
        services.AddModuleDatabase<IdentityDbContext>("core.identity", IdentityDbContext.SchemaName, o => new IdentityDbContext(o));
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton(TimeProvider.System);
        return services.BuildServiceProvider();
    }

    private async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        return c;
    }

    private static async Task<PostgresException> ExpectPgError(NpgsqlConnection c, string sql)
    {
        return await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, sql));
    }

    [Fact]
    public async Task Bootstrap_and_migrations_are_idempotent()
    {
        PostgresFixture.SkipIfUnavailable();

        await DatabaseBootstrapper.BootstrapAsync(PostgresFixture.AdminConnectionString!, db.DatabaseName, db.Roles, db.MigratorPassword, db.AppPassword, TestContext.Current.CancellationToken);
        await MigrateAsync();
        await MigrateAsync();

        await using var c = await OpenAsync(db.MigratorConnectionString);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM core_identity.\"__ef_migrations_history\"", c);
        Assert.Equal(1L, await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();

        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(db.MigratorConnectionString).Options;
        await using var ctx = new IdentityDbContext(options);
        Assert.False(ctx.Database.HasPendingModelChanges(), "Hand-written migration/snapshot does not match the model.");
    }

    [Fact]
    public async Task Platform_roles_are_not_privileged()
    {
        PostgresFixture.SkipIfUnavailable();

        await using var c = await OpenAsync(db.AdminDatabaseConnectionString);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_roles WHERE rolname LIKE @p AND (rolsuper OR rolbypassrls OR rolinherit)", c);
        cmd.Parameters.AddWithValue("p", db.Roles.Prefix + "\\_%");
        Assert.Equal(0L, await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task App_login_without_module_role_has_no_access()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();

        await using var c = await OpenAsync(db.AppConnectionString);
        var ex = await ExpectPgError(c, "SELECT * FROM core_identity.user_account");
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task App_login_cannot_become_the_migrator()
    {
        PostgresFixture.SkipIfUnavailable();

        await using var c = await OpenAsync(db.AppConnectionString);
        var ex = await ExpectPgError(c, $"SET ROLE \"{db.Roles.Migrator}\"");
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Module_role_cannot_read_another_modules_schema()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();

        // A second module schema with a table, prepared exactly as the migrator would.
        await ModuleMigrator.PrepareSchemaAsync(db.MigratorConnectionString, db.Roles, "other_module", TestContext.Current.CancellationToken);
        await using (var m = await OpenAsync(db.MigratorConnectionString))
        {
            await PostgresFixture.Exec(m, "CREATE TABLE IF NOT EXISTS other_module.secret (id int)");
        }

        await using var c = await OpenAsync(db.AppConnectionString);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(IdentityDbContext.SchemaName)}\"");
        var ex = await ExpectPgError(c, "SELECT * FROM other_module.secret");
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Module_role_cannot_touch_migration_history()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();

        await using var c = await OpenAsync(db.AppConnectionString);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(IdentityDbContext.SchemaName)}\"");
        var ex = await ExpectPgError(c, "DELETE FROM core_identity.\"__ef_migrations_history\"");
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task User_provisioning_is_idempotent_and_updates_profile()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();
        await using var sp = BuildServices();

        Guid first, second;
        await using (var scope = sp.CreateAsyncScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
                .EnsureUserAsync(new ExternalIdentity("https://idp.test/realms/x", "synthetic-sub-1", "Synthetic One", null), TestContext.Current.CancellationToken);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
                .EnsureUserAsync(new ExternalIdentity("https://idp.test/realms/x", "synthetic-sub-1", "Synthetic Renamed", null), TestContext.Current.CancellationToken);
        }

        Assert.Equal(first, second);

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await ctx.Users.SingleAsync(u => u.Id == first, TestContext.Current.CancellationToken);
            Assert.Equal("Synthetic Renamed", user.DisplayName);
        }
    }

    [Fact]
    public async Task Same_subject_from_different_issuers_are_different_users()
    {
        PostgresFixture.SkipIfUnavailable();
        await MigrateAsync();
        await using var sp = BuildServices();
        await using var scope = sp.CreateAsyncScope();
        var dir = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var a = await dir.EnsureUserAsync(new ExternalIdentity("https://idp-a.test", "same-sub", null, null), TestContext.Current.CancellationToken);
        var b = await dir.EnsureUserAsync(new ExternalIdentity("https://idp-b.test", "same-sub", null, null), TestContext.Current.CancellationToken);

        Assert.NotEqual(a, b);
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>;
