using System.Text.RegularExpressions;

namespace HealthOS.Platform.Data;

/// <summary>
/// Role naming for one HealthOS installation. The prefix lets several installations (or test runs)
/// share a PostgreSQL cluster without colliding, because roles are cluster-wide.
/// </summary>
public sealed partial record DatabaseRoles
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,20}$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,40}$")]
    private static partial Regex SchemaPattern();

    public DatabaseRoles(string prefix = "hos")
    {
        if (!PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException("Role prefix must be lowercase letters, digits or underscores.", nameof(prefix));
        }

        Prefix = prefix;
    }

    public string Prefix { get; }

    /// <summary>Owns module schemas and runs migrations. Never used by the running application.</summary>
    public string Migrator => $"{Prefix}_migrator";

    /// <summary>Application login. NOINHERIT and no privileges of its own: it must SET ROLE to a module role.</summary>
    public string App => $"{Prefix}_app";

    /// <summary>Group role holding a single module's privileges on that module's schema.</summary>
    public string ForModuleSchema(string schema)
    {
        if (!SchemaPattern().IsMatch(schema))
        {
            throw new ArgumentException($"Invalid schema name '{schema}'.", nameof(schema));
        }

        return $"{Prefix}_mod_{schema}";
    }

    /// <summary>Quotes an identifier that has already been validated by this type's patterns.</summary>
    internal static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
