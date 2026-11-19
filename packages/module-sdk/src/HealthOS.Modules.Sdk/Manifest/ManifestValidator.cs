using System.Text.RegularExpressions;
using Semver;

namespace HealthOS.Modules.Sdk.Manifest;

public sealed record ManifestIssue(string ModuleId, string Code, string Message)
{
    public override string ToString() => $"[{Code}] {ModuleId}: {Message}";
}

/// <summary>Validates a single manifest in isolation (no knowledge of other modules).</summary>
public static partial class ManifestValidator
{
    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex ModuleIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*){2,}$")]
    private static partial Regex PermissionPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaPattern();

    private static readonly HashSet<string> ReservedSchemas = ["public", "information_schema"];

    public static IReadOnlyList<ManifestIssue> Validate(ModuleManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var issues = new List<ManifestIssue>();
        void Add(string code, string message) => issues.Add(new ManifestIssue(m.Id, code, message));

        if (!ModuleIdPattern().IsMatch(m.Id))
        {
            Add("INVALID_ID", "Id must be lowercase dot-separated segments, e.g. 'careops.quality'.");
        }

        if (!SemVersion.TryParse(m.Version, SemVersionStyles.Strict, out _))
        {
            Add("INVALID_VERSION", $"'{m.Version}' is not a strict semantic version.");
        }

        if (!SemVersionRange.TryParseNpm(m.RequiresCore, out _))
        {
            Add("INVALID_RANGE", $"requiresCore '{m.RequiresCore}' is not a valid version range.");
        }

        if (string.IsNullOrWhiteSpace(m.DisplayName))
        {
            Add("MISSING_DISPLAY_NAME", "displayName is required.");
        }

        var seen = new HashSet<string>();
        foreach (var d in m.Dependencies.Concat(m.OptionalDependencies))
        {
            if (d.Id == m.Id)
            {
                Add("SELF_DEPENDENCY", "A module cannot depend on itself.");
            }

            if (!seen.Add(d.Id))
            {
                Add("DUPLICATE_DEPENDENCY", $"Dependency '{d.Id}' is declared more than once.");
            }

            if (!SemVersionRange.TryParseNpm(d.Version, out _))
            {
                Add("INVALID_RANGE", $"Dependency '{d.Id}' range '{d.Version}' is invalid.");
            }
        }

        var needsLicense = m.Kind is ModuleKind.Product or ModuleKind.Pack or ModuleKind.Bridge or ModuleKind.Ai;
        if (needsLicense && m.License is null)
        {
            Add("MISSING_LICENSE", $"Modules of kind '{m.Kind}' must declare a license feature.");
        }

        if (m.Kind == ModuleKind.Platform && m.License is not null)
        {
            Add("UNEXPECTED_LICENSE", "Platform modules are not separately licensed.");
        }

        var namespacePrefix = m.Id.Split('.')[0] + ".";
        var permissionCodes = new HashSet<string>();
        foreach (var p in m.Permissions)
        {
            if (!PermissionPattern().IsMatch(p.Code))
            {
                Add("INVALID_PERMISSION", $"Permission '{p.Code}' must have at least three lowercase segments.");
            }
            else if (!p.Code.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                Add("PERMISSION_NAMESPACE", $"Permission '{p.Code}' must start with '{namespacePrefix}'.");
            }

            if (p.Scope == PermissionScope.Platform && m.Kind != ModuleKind.Platform)
            {
                Add("PLATFORM_PERMISSION", $"Permission '{p.Code}' has platform scope but '{m.Id}' is not a platform module.");
            }

            if (!permissionCodes.Add(p.Code))
            {
                Add("DUPLICATE_PERMISSION", $"Permission '{p.Code}' is declared more than once.");
            }
        }

        foreach (var route in m.Ui.Routes)
        {
            if (route.Permission is not null && !permissionCodes.Contains(route.Permission))
            {
                Add("UNKNOWN_ROUTE_PERMISSION", $"UI route '{route.Path}' uses undeclared permission '{route.Permission}'.");
            }
        }

        if (m.Database is { } db &&
            (!SchemaPattern().IsMatch(db.Schema) || ReservedSchemas.Contains(db.Schema) ||
             db.Schema.StartsWith("pg_", StringComparison.Ordinal)))
        {
            Add("INVALID_SCHEMA", $"Database schema '{db.Schema}' is invalid or reserved.");
        }

        var settingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in m.Settings)
        {
            if (!s.Key.StartsWith(namespacePrefix, StringComparison.Ordinal) || s.Key.Split('.').Length < 3)
            {
                Add("SETTING_NAMESPACE", $"Setting '{s.Key}' must start with '{namespacePrefix}' and have at least three segments.");
            }

            if (!settingKeys.Add(s.Key))
            {
                Add("DUPLICATE_SETTING", $"Setting '{s.Key}' is declared more than once.");
            }

            if (s.AllowedScopes.Count == 0)
            {
                Add("SETTING_SCOPES", $"Setting '{s.Key}' must allow at least one scope.");
            }

            if (s.Type == HealthOS.Modules.Sdk.Configuration.SettingType.Enum && s.Options.Count == 0)
            {
                Add("SETTING_OPTIONS", $"Enum setting '{s.Key}' needs options.");
            }

            foreach (var problem in HealthOS.Modules.Sdk.Configuration.ConfigResolver.ValidateValue(s, s.Default))
            {
                Add("SETTING_DEFAULT", $"Default of '{s.Key}' {problem}.");
            }
        }

        if (m.DataRetention.DestructiveUninstall)
        {
            Add("DESTRUCTIVE_UNINSTALL", "Destructive uninstall is not permitted by default (spec §3.1).");
        }

        return issues;
    }
}
