using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Manifest;
using static HealthOS.Modules.Sdk.Tests.TestManifests;

namespace HealthOS.Modules.Sdk.Tests;

public class PackageResolverTests
{
    private const string Core = "1.0.0";

    private static readonly ModuleManifest Identity = Platform("core.identity");
    private static readonly ModuleManifest Documents = Platform("core.documents", ("core.identity", "^1.0.0"));

    private static IReadOnlyList<string> Codes(PackageResolution r) => r.Issues.Select(i => i.Code).ToList();

    [Fact]
    public void Orders_modules_by_dependency()
    {
        var careops = Product("careops.quality", ("core.documents", "^1.0.0"));
        var r = PackageResolver.Resolve([careops, Documents, Identity], Core);

        Assert.True(r.Succeeded, string.Join("; ", r.Issues));
        Assert.Equal(["core.identity", "core.documents", "careops.quality"], r.OrderedModules.Select(m => m.Id));
    }

    [Fact]
    public void Missing_dependency_is_reported()
    {
        var careops = Product("careops.quality", ("core.documents", "^1.0.0"));
        var r = PackageResolver.Resolve([careops, Identity], Core);

        Assert.Contains("MISSING_DEPENDENCY", Codes(r));
        Assert.Empty(r.OrderedModules);
    }

    [Fact]
    public void Version_incompatibility_is_reported()
    {
        var careops = Product("careops.quality", ("core.documents", "^2.0.0"));
        var r = PackageResolver.Resolve([careops, Documents, Identity], Core);
        Assert.Contains("VERSION_INCOMPATIBLE", Codes(r));
    }

    [Fact]
    public void Core_incompatibility_is_reported()
    {
        var future = Make("careops.quality", ModuleKind.Product, requiresCore: ">=2.0.0", schema: "careops_quality");
        Assert.Contains("CORE_INCOMPATIBLE", Codes(PackageResolver.Resolve([future], Core)));
    }

    [Fact]
    public void Cycles_are_reported()
    {
        var a = Platform("core.alpha", ("core.beta", "^1.0.0"));
        var b = Platform("core.beta", ("core.alpha", "^1.0.0"));
        Assert.Contains("CYCLE", Codes(PackageResolver.Resolve([a, b], Core)));
    }

    [Fact]
    public void Product_cannot_depend_on_another_product()
    {
        var clinic = Product("clinicos.core");
        var careops = Product("careops.quality", ("clinicos.core", "^1.0.0"));
        Assert.Contains("KIND_RULE", Codes(PackageResolver.Resolve([clinic, careops], Core)));
    }

    [Fact]
    public void Product_may_depend_on_same_family_product()
    {
        var clinic = Product("clinicos.core");
        var billing = Product("clinicos.billing", ("clinicos.core", "^1.0.0"));
        Assert.True(PackageResolver.Resolve([clinic, billing], Core).Succeeded);
    }

    [Fact]
    public void Platform_cannot_depend_on_product()
    {
        var clinic = Product("clinicos.core");
        var audit = Platform("core.audit", ("clinicos.core", "^1.0.0"));
        Assert.Contains("KIND_RULE", Codes(PackageResolver.Resolve([clinic, audit], Core)));
    }

    [Fact]
    public void Pack_requires_exactly_one_parent_product()
    {
        var orphan = Make("specialty.dental", ModuleKind.Pack, schema: "specialty_dental");
        Assert.Contains("KIND_RULE", Codes(PackageResolver.Resolve([orphan], Core)));

        var clinic = Product("clinicos.core");
        var dental = Make("specialty.dental", ModuleKind.Pack, schema: "specialty_dental", deps: [("clinicos.core", "^1.0.0")]);
        Assert.True(PackageResolver.Resolve([clinic, dental], Core).Succeeded);
    }

    [Fact]
    public void Bridge_requires_two_product_families()
    {
        var clinic = Product("clinicos.core");
        var careops = Product("careops.quality");
        var bridge = Make("bridge.clinicos-careops", ModuleKind.Bridge, deps: [("clinicos.core", "^1.0.0"), ("careops.quality", "^1.0.0")]);
        Assert.True(PackageResolver.Resolve([clinic, careops, bridge], Core).Succeeded);

        var halfBridge = Make("bridge.clinicos-only", ModuleKind.Bridge, deps: [("clinicos.core", "^1.0.0")]);
        Assert.Contains("KIND_RULE", Codes(PackageResolver.Resolve([clinic, halfBridge], Core)));
    }

    [Fact]
    public void Two_modules_cannot_own_the_same_schema()
    {
        var a = Make("careops.quality", ModuleKind.Product, schema: "shared");
        var b = Make("clinicos.core", ModuleKind.Product, schema: "shared");
        Assert.Contains("SCHEMA_CONFLICT", Codes(PackageResolver.Resolve([a, b], Core)));
    }

    [Fact]
    public void Duplicate_modules_are_reported() =>
        Assert.Contains("DUPLICATE_MODULE", Codes(PackageResolver.Resolve([Identity, Identity], Core)));
}
