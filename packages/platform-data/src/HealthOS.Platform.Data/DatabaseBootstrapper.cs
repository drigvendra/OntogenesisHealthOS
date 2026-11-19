using Npgsql;
using static HealthOS.Platform.Data.DatabaseRoles;

namespace HealthOS.Platform.Data;

/// <summary>
/// One-time, idempotent setup run with an administrative connection: creates the database, the
/// migrator and application login roles, and removes default PUBLIC privileges.
/// Neither role is SUPERUSER or BYPASSRLS. See docs/adrs/0002-tenancy-and-isolation.md.
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task BootstrapAsync(
        string adminConnectionString,
        string databaseName,
        DatabaseRoles roles,
        string migratorPassword,
        string appPassword,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, "^[a-z][a-z0-9_]{0,62}$"))
        {
            throw new ArgumentException("Database name must be lowercase letters, digits or underscores.", nameof(databaseName));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(migratorPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(appPassword);

        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync(ct).ConfigureAwait(false);

            if (!await ExistsAsync(admin, "SELECT 1 FROM pg_database WHERE datname = @n", databaseName, ct).ConfigureAwait(false))
            {
                await ExecAsync(admin, $"CREATE DATABASE {Q(databaseName)}", ct).ConfigureAwait(false);
            }

            await EnsureLoginRoleAsync(admin, roles.Migrator, migratorPassword, "CREATEROLE NOINHERIT", ct).ConfigureAwait(false);
            await EnsureLoginRoleAsync(admin, roles.App, appPassword, "NOCREATEROLE NOINHERIT", ct).ConfigureAwait(false);

            await ExecAsync(admin, $"REVOKE ALL ON DATABASE {Q(databaseName)} FROM PUBLIC", ct).ConfigureAwait(false);
            await ExecAsync(admin, $"GRANT CONNECT, CREATE ON DATABASE {Q(databaseName)} TO {Q(roles.Migrator)}", ct).ConfigureAwait(false);
            await ExecAsync(admin, $"GRANT CONNECT ON DATABASE {Q(databaseName)} TO {Q(roles.App)}", ct).ConfigureAwait(false);
        }

        // Inside the target database: nobody gets implicit access to the public schema.
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
        await using var db = new NpgsqlConnection(builder.ConnectionString);
        await db.OpenAsync(ct).ConfigureAwait(false);
        await ExecAsync(db, "REVOKE ALL ON SCHEMA public FROM PUBLIC", ct).ConfigureAwait(false);
    }

    private static async Task EnsureLoginRoleAsync(NpgsqlConnection admin, string role, string password, string attributes, CancellationToken ct)
    {
        var verb = await ExistsAsync(admin, "SELECT 1 FROM pg_roles WHERE rolname = @n", role, ct).ConfigureAwait(false)
            ? "ALTER"
            : "CREATE";

        // Passwords cannot be bound as parameters in DDL; use a quoted literal built by Npgsql-safe escaping.
        var literal = "'" + password.Replace("'", "''", StringComparison.Ordinal) + "'";
        await ExecAsync(
            admin,
            $"{verb} ROLE {Q(role)} WITH LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOREPLICATION {attributes} PASSWORD {literal}",
            ct).ConfigureAwait(false);
    }

    internal static async Task<bool> ExistsAsync(NpgsqlConnection c, string sql, string name, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("n", name);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    internal static async Task ExecAsync(NpgsqlConnection c, string sql, CancellationToken ct)
    {
#pragma warning disable CA2100 // Identifiers are validated by DatabaseRoles and quoted; no user input reaches this SQL.
        await using var cmd = new NpgsqlCommand(sql, c);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
