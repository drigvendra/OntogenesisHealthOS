using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy.Data;
using HealthOS.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>Database foundation: migrations, roles and per-module schema isolation.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class DatabaseIsolationTests(PostgresFixture db)
{
    private const string InsufficientPrivilege = "42501";

    private async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        return c;
    }

    private static async Task<PostgresException> ExpectPgError(NpgsqlConnection c, string sql) =>
        await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, sql));

    [Fact]
    public async Task Bootstrap_and_migrations_are_idempotent()
    {
        PostgresFixture.SkipIfUnavailable();

        await DatabaseBootstrapper.BootstrapAsync(PostgresFixture.AdminConnectionString!, db.DatabaseName, db.Roles, db.MigratorPassword, db.AppPassword, TestContext.Current.CancellationToken);
        await db.MigrateAsync();
        await db.MigrateAsync();

        await using var c = await OpenAsync(db.MigratorConnectionString);
        Assert.Equal(3L, await PostgresFixture.Scalar(c, "SELECT count(*) FROM core_identity.schema_migrations"));
        Assert.Equal(1L, await PostgresFixture.Scalar(c, "SELECT count(*) FROM core_tenancy.schema_migrations"));
    }

    [Fact]
    public async Task Editing_an_applied_migration_is_detected()
    {
        PostgresFixture.SkipIfUnavailable();

        await using var c = await OpenAsync(db.MigratorConnectionString);
        var original = (string)(await PostgresFixture.Scalar(c, "SELECT checksum FROM core_tenancy.schema_migrations WHERE version = 1"))!;
        await PostgresFixture.Exec(c, "UPDATE core_tenancy.schema_migrations SET checksum = 'tampered' WHERE version = 1");
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(db.MigrateAsync);
            Assert.Contains("was changed after it was applied", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            await PostgresFixture.Exec(c, $"UPDATE core_tenancy.schema_migrations SET checksum = '{original}' WHERE version = 1");
        }
    }

    [Fact]
    public async Task Ef_models_match_the_migrated_schema()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        await using var c = await OpenAsync(db.AdminDatabaseConnectionString);

        var problems = new List<string>();
        foreach (DbContext ctx in new DbContext[]
                 {
                     scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
                     scope.ServiceProvider.GetRequiredService<TenancyDbContext>(),
                     scope.ServiceProvider.GetRequiredService<HealthOS.Core.Audit.AuditDbContext>(),
                     scope.ServiceProvider.GetRequiredService<HealthOS.Core.Licensing.LicensingDbContext>(),
                     scope.ServiceProvider.GetRequiredService<HealthOS.Core.Modules.ModulesDbContext>(),
                     scope.ServiceProvider.GetRequiredService<HealthOS.Core.Configuration.ConfigurationDbContext>(),
                 })
        {
            foreach (var entity in ctx.Model.GetEntityTypes())
            {
                var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(table)!;
                    await using var cmd = new NpgsqlCommand(
                        "SELECT is_nullable FROM information_schema.columns WHERE table_schema = @s AND table_name = @t AND column_name = @c", c);
                    cmd.Parameters.AddWithValue("s", table.Schema!);
                    cmd.Parameters.AddWithValue("t", table.Name);
                    cmd.Parameters.AddWithValue("c", column);
                    var nullable = (string?)await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
                    if (nullable is null)
                    {
                        problems.Add($"{table.Schema}.{table.Name}.{column} missing");
                    }
                    else if ((nullable == "YES") != property.IsNullable)
                    {
                        problems.Add($"{table.Schema}.{table.Name}.{column} nullability differs (db {nullable}, model {property.IsNullable})");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Platform_roles_are_not_privileged()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = await OpenAsync(db.AdminDatabaseConnectionString);
        var prefix = db.Roles.Prefix + "\\_%";

        Assert.Equal(0L, await PostgresFixture.Scalar(c, $"SELECT count(*) FROM pg_roles WHERE rolname LIKE '{prefix}' AND (rolsuper OR rolbypassrls OR rolcreatedb)"));
        // Login roles never inherit privileges.
        Assert.Equal(0L, await PostgresFixture.Scalar(c, $"SELECT count(*) FROM pg_roles WHERE rolname IN ('{db.Roles.App}', '{db.Roles.Migrator}') AND rolinherit"));
        // Module roles are members of nothing except the platform-services group.
        Assert.Equal(0L, await PostgresFixture.Scalar(c, $"""
            SELECT count(*) FROM pg_auth_members m
              JOIN pg_roles member ON member.oid = m.member
              JOIN pg_roles grp ON grp.oid = m.roleid
             WHERE member.rolname LIKE '{db.Roles.Prefix}\_mod\_%' AND grp.rolname <> '{db.Roles.PlatformServices}'
            """));
    }

    [Fact]
    public async Task App_login_without_module_role_has_no_access()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = await OpenAsync(db.AppConnectionString);
        Assert.Equal(InsufficientPrivilege, (await ExpectPgError(c, "SELECT * FROM core_identity.user_account")).SqlState);
        Assert.Equal(InsufficientPrivilege, (await ExpectPgError(c, "SELECT * FROM core_tenancy.tenant")).SqlState);
    }

    [Fact]
    public async Task App_login_cannot_become_the_migrator()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = await OpenAsync(db.AppConnectionString);
        Assert.Equal(InsufficientPrivilege, (await ExpectPgError(c, $"SET ROLE \"{db.Roles.Migrator}\"")).SqlState);
    }

    [Fact]
    public async Task Module_role_cannot_read_another_modules_schema()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = await OpenAsync(db.AppConnectionString);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(IdentityDbContext.SchemaName)}\"");
        Assert.Equal(InsufficientPrivilege, (await ExpectPgError(c, "SELECT * FROM core_tenancy.tenant")).SqlState);
    }

    [Fact]
    public async Task Module_role_cannot_touch_migration_history()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = await OpenAsync(db.AppConnectionString);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(TenancyDbContext.SchemaName)}\"");
        Assert.Equal(InsufficientPrivilege, (await ExpectPgError(c, "DELETE FROM core_tenancy.schema_migrations")).SqlState);
    }

    [Fact]
    public async Task User_provisioning_is_idempotent_and_updates_profile()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var subject = "synthetic-" + Guid.NewGuid().ToString("N");

        Guid first, second;
        await using (var scope = sp.CreateAsyncScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
                .EnsureUserAsync(new ExternalIdentity("https://idp.test/realms/x", subject, "Synthetic One", null), TestContext.Current.CancellationToken);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
                .EnsureUserAsync(new ExternalIdentity("https://idp.test/realms/x", subject, "Synthetic Renamed", null), TestContext.Current.CancellationToken);
        }

        Assert.Equal(first, second);
        await using (var scope = sp.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.SingleAsync(u => u.Id == first, TestContext.Current.CancellationToken);
            Assert.Equal("Synthetic Renamed", user.DisplayName);
        }
    }

    [Fact]
    public async Task Same_subject_from_different_issuers_are_different_users()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        var dir = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var subject = "same-" + Guid.NewGuid().ToString("N");

        var a = await dir.EnsureUserAsync(new ExternalIdentity("https://idp-a.test", subject, null, null), TestContext.Current.CancellationToken);
        var b = await dir.EnsureUserAsync(new ExternalIdentity("https://idp-b.test", subject, null, null), TestContext.Current.CancellationToken);

        Assert.NotEqual(a, b);
    }
}
