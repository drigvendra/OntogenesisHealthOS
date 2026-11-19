using System.Text.Json.Serialization;

namespace HealthOS.Modules.Sdk.Manifest;

/// <summary>
/// Declarative description of a module, read from the module's <c>module.json</c>.
/// See docs/architecture/module-boundaries.md and schema/module-manifest.v1.schema.json.
/// </summary>
public sealed record ModuleManifest
{
    public required string Id { get; init; }
    public required ModuleKind Kind { get; init; }
    public required string Version { get; init; }
    public required string DisplayName { get; init; }
    public required string RequiresCore { get; init; }
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];
    public IReadOnlyList<ModuleDependency> OptionalDependencies { get; init; } = [];
    public IReadOnlyList<string> IncompatibleWith { get; init; } = [];
    public ModuleLicense? License { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<PermissionDeclaration> Permissions { get; init; } = [];
    public ModuleContracts Contracts { get; init; } = new();
    public ModuleDatabase? Database { get; init; }
    public ModuleUi Ui { get; init; } = new();
    public ModuleDataRetention DataRetention { get; init; } = new();
    public IReadOnlyList<string> HealthChecks { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ModuleKind>))]
public enum ModuleKind
{
    Platform,
    Domain,
    Product,
    Pack,
    Bridge,
    Ai,
}

public sealed record ModuleDependency(string Id, string Version);

public sealed record ModuleLicense(string ProductCode, string Feature);

public sealed record PermissionDeclaration(
    string Code,
    string Description,
    bool RequiresESignature = false,
    PermissionScope Scope = PermissionScope.Tenant,
    bool RequiresMfa = false);

/// <summary>
/// Tenant permissions are granted through tenant roles. Platform permissions belong to Ontogenesis
/// platform operators only, always require MFA, and may only be declared by platform modules.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PermissionScope>))]
public enum PermissionScope
{
    Tenant,
    Platform,
}

public sealed record ModuleContracts
{
    public ContractSet Provides { get; init; } = new();
    public ContractSet Consumes { get; init; } = new();
}

public sealed record ContractSet
{
    public IReadOnlyList<ApiContract> Api { get; init; } = [];
    public IReadOnlyList<EventContract> Events { get; init; } = [];
}

public sealed record ApiContract(string Name, string Version, string? Spec = null);

public sealed record EventContract(string Type, int Version, string? Schema = null);

public sealed record ModuleDatabase(string Schema);

public sealed record ModuleUi
{
    public IReadOnlyList<UiRoute> Routes { get; init; } = [];
}

public sealed record UiRoute(string Path, string App, string? Permission = null);

public sealed record ModuleDataRetention
{
    public string DefaultPolicy { get; init; } = "none";
    public bool DestructiveUninstall { get; init; }
}
