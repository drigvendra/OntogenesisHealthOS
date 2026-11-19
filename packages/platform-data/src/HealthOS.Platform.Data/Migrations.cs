using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using static HealthOS.Platform.Data.DatabaseRoles;

namespace HealthOS.Platform.Data;

/// <summary>Applies one module's schema changes. Registered by each module that owns tables.</summary>
public interface IModuleMigrations
{
    string ModuleId { get; }

    string Schema { get; }

    Task MigrateAsync(string migratorConnectionString, DatabaseRoles roles, CancellationToken ct);
}

public sealed record SqlMigration(int Version, string Name, string Sql, string Checksum);

/// <summary>
/// SQL-first migrations. Scripts are embedded resources named "...Migrations.NNNN_description.sql",
/// applied in version order, once, in a transaction each, recorded with a checksum in
/// &lt;schema&gt;.schema_migrations. Editing an applied script is detected and refused.
///
/// Placeholders (already-quoted identifiers): {{schema}}, {{module_role}}, {{migrator_role}},
/// {{app_role}}, {{platform_services_role}}.
/// </summary>
public sealed partial class SqlModuleMigrations(string moduleId, string schema, Assembly assembly) : IModuleMigrations
{
    [GeneratedRegex(@"\.Migrations\.(\d{4})_([A-Za-z0-9_]+)\.sql$")]
    private static partial Regex ResourcePattern();

    public string ModuleId { get; } = moduleId;

    public string Schema { get; } = schema;

    public IReadOnlyList<SqlMigration> LoadScripts()
    {
        var scripts = new List<SqlMigration>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var match = ResourcePattern().Match(name);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
            var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            scripts.Add(new SqlMigration(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups[2].Value, sql, checksum));
        }

        var duplicates = scripts.GroupBy(s => s.Version).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Module '{ModuleId}' has duplicate migration versions: {string.Join(", ", duplicates)}.");
        }

        return scripts.OrderBy(s => s.Version).ToList();
    }

    public async Task MigrateAsync(string migratorConnectionString, DatabaseRoles roles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var scripts = LoadScripts();
        var history = $"{Q(Schema)}.\"schema_migrations\"";

        await using var c = new NpgsqlConnection(migratorConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);

        await DatabaseBootstrapper.ExecAsync(c, $"""
            CREATE TABLE IF NOT EXISTS {history} (
                version     integer PRIMARY KEY,
                name        text NOT NULL,
                checksum    text NOT NULL,
                applied_at  timestamptz NOT NULL DEFAULT now())
            """, ct).ConfigureAwait(false);
        // Migration history is internal to the migrator, even though default privileges would grant it.
        await DatabaseBootstrapper.ExecAsync(c, $"REVOKE ALL ON TABLE {history} FROM {Q(roles.ForModuleSchema(Schema))}", ct).ConfigureAwait(false);

        var applied = new Dictionary<int, string>();
        await using (var cmd = new NpgsqlCommand($"SELECT version, checksum FROM {history}", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                applied[r.GetInt32(0)] = r.GetString(1);
            }
        }

        foreach (var script in scripts)
        {
            if (applied.TryGetValue(script.Version, out var existing))
            {
                if (existing != script.Checksum)
                {
                    throw new InvalidOperationException(
                        $"Migration {ModuleId} {script.Version:D4}_{script.Name} was changed after it was applied. Add a new migration instead.");
                }

                continue;
            }

            await using var tx = await c.BeginTransactionAsync(ct).ConfigureAwait(false);
            await DatabaseBootstrapper.ExecAsync(c, Render(script.Sql, roles), ct).ConfigureAwait(false);
            await using (var record = new NpgsqlCommand($"INSERT INTO {history} (version, name, checksum) VALUES (@v, @n, @c)", c, tx))
            {
                record.Parameters.AddWithValue("v", script.Version);
                record.Parameters.AddWithValue("n", script.Name);
                record.Parameters.AddWithValue("c", script.Checksum);
                await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private string Render(string sql, DatabaseRoles roles) => sql
        .Replace("{{schema}}", Q(Schema), StringComparison.Ordinal)
        .Replace("{{module_role}}", Q(roles.ForModuleSchema(Schema)), StringComparison.Ordinal)
        .Replace("{{migrator_role}}", Q(roles.Migrator), StringComparison.Ordinal)
        .Replace("{{app_role}}", Q(roles.App), StringComparison.Ordinal)
        .Replace("{{platform_services_role}}", Q(roles.PlatformServices), StringComparison.Ordinal);
}

/// <summary>
/// Runs as the migrator role: ensures each module's schema and module role, wires default privileges
/// so the module role can use tables the migrator creates, then applies the module's migrations. Idempotent.
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
            await module.MigrateAsync(migratorConnectionString, roles, ct).ConfigureAwait(false);
        }
    }

    public static async Task PrepareSchemaAsync(string migratorConnectionString, DatabaseRoles roles, string schema, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var moduleRole = roles.ForModuleSchema(schema);
        await using var c = new NpgsqlConnection(migratorConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);

        // Group role for shared platform services (e.g. appending audit events). Module roles inherit
        // only this role's privileges; nothing else is ever granted to it except EXECUTE on audited routines.
        if (!await DatabaseBootstrapper.ExistsAsync(c, "SELECT 1 FROM pg_roles WHERE rolname = @n", roles.PlatformServices, ct).ConfigureAwait(false))
        {
            await DatabaseBootstrapper.ExecAsync(c, $"CREATE ROLE {Q(roles.PlatformServices)} NOLOGIN NOINHERIT NOBYPASSRLS", ct).ConfigureAwait(false);
        }

        if (!await DatabaseBootstrapper.ExistsAsync(c, "SELECT 1 FROM pg_roles WHERE rolname = @n", moduleRole, ct).ConfigureAwait(false))
        {
            await DatabaseBootstrapper.ExecAsync(c, $"CREATE ROLE {Q(moduleRole)} NOLOGIN INHERIT NOBYPASSRLS", ct).ConfigureAwait(false);
        }

        await DatabaseBootstrapper.ExecAsync(c, $"GRANT {Q(roles.PlatformServices)} TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        // The application login may switch to (but does not inherit) the module role.
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT {Q(moduleRole)} TO {Q(roles.App)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"CREATE SCHEMA IF NOT EXISTS {Q(schema)} AUTHORIZATION {Q(roles.Migrator)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"REVOKE ALL ON SCHEMA {Q(schema)} FROM PUBLIC", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"GRANT USAGE ON SCHEMA {Q(schema)} TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"ALTER DEFAULT PRIVILEGES FOR ROLE {Q(roles.Migrator)} IN SCHEMA {Q(schema)} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"ALTER DEFAULT PRIVILEGES FOR ROLE {Q(roles.Migrator)} IN SCHEMA {Q(schema)} GRANT USAGE, SELECT ON SEQUENCES TO {Q(moduleRole)}", ct).ConfigureAwait(false);
        await DatabaseBootstrapper.ExecAsync(c, $"ALTER DEFAULT PRIVILEGES FOR ROLE {Q(roles.Migrator)} IN SCHEMA {Q(schema)} REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC", ct).ConfigureAwait(false);
    }
}
