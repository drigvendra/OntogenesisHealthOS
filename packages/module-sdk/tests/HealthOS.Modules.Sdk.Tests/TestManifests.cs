using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.Modules.Sdk.Tests;

internal static class TestManifests
{
    public static ModuleManifest Make(
        string id,
        ModuleKind kind,
        string version = "1.0.0",
        string requiresCore = ">=1.0.0 <2.0.0",
        string? schema = null,
        params (string Id, string Range)[] deps)
    {
        var needsLicense = kind is ModuleKind.Product or ModuleKind.Pack or ModuleKind.Bridge or ModuleKind.Ai;
        return new ModuleManifest
        {
            Id = id,
            Kind = kind,
            Version = version,
            DisplayName = id,
            RequiresCore = requiresCore,
            Dependencies = deps.Select(d => new ModuleDependency(d.Id, d.Range)).ToList(),
            License = needsLicense ? new ModuleLicense(id.Split('.')[0].ToUpperInvariant(), id) : null,
            Database = schema is null ? null : new ModuleDatabase(schema),
        };
    }

    public static ModuleManifest Platform(string id, params (string, string)[] deps) =>
        Make(id, ModuleKind.Platform, schema: id.Replace('.', '_').Replace('-', '_'), deps: deps);

    public static ModuleManifest Product(string id, params (string, string)[] deps) =>
        Make(id, ModuleKind.Product, schema: id.Replace('.', '_').Replace('-', '_'), deps: deps);
}
