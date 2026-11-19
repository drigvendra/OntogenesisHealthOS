using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Manifest;
using static HealthOS.Modules.Sdk.Tests.TestManifests;

namespace HealthOS.Modules.Sdk.Tests;

public class TenantActivationTests
{
    private static readonly ModuleManifest Core = Platform("core.documents");
    private static readonly ModuleManifest Clinic = Product("clinicos.core", ("core.documents", "^1.0.0"));
    private static readonly ModuleManifest Dental = Make("specialty.dental", ModuleKind.Pack, schema: "specialty_dental", deps: [("clinicos.core", "^1.0.0")]);
    private static readonly ModuleManifest Quality = Product("careops.quality");
    private static readonly ModuleManifest[] Package = [Core, Clinic, Dental, Quality];

    private static LicenseState AllLicensed(ModuleLicense _) => LicenseState.Active;

    private static Dictionary<string, ModuleActivation> Resolve(IReadOnlySet<string> enabled, Func<ModuleLicense, LicenseState>? license = null) =>
        TenantActivation.Resolve(Package, enabled, license ?? AllLicensed).ToDictionary(a => a.Id);

    private static IReadOnlySet<string> Set(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);

    [Fact]
    public void Platform_modules_are_always_active()
    {
        var r = Resolve(Set());
        Assert.Equal(ActivationState.Active, r["core.documents"].State);
        Assert.Equal(ActivationState.Inactive, r["clinicos.core"].State);
        Assert.Contains(r["clinicos.core"].Reasons, x => x.Code == "NOT_ENABLED");
    }

    [Fact]
    public void Enabled_and_licensed_module_is_active()
    {
        Assert.Equal(ActivationState.Active, Resolve(Set("clinicos.core"))["clinicos.core"].State);
    }

    [Fact]
    public void Unlicensed_module_is_inactive_even_when_enabled()
    {
        var r = Resolve(Set("clinicos.core"), _ => LicenseState.None);
        Assert.Equal(ActivationState.Inactive, r["clinicos.core"].State);
        Assert.Contains(r["clinicos.core"].Reasons, x => x.Code == "NOT_ENTITLED");
    }

    [Fact]
    public void Expired_licence_makes_module_and_its_dependents_read_only()
    {
        var r = Resolve(Set("clinicos.core", "specialty.dental"), l => l.Feature == "clinicos.core" ? LicenseState.ReadOnly : LicenseState.Active);
        Assert.Equal(ActivationState.ReadOnly, r["clinicos.core"].State);
        Assert.Equal(ActivationState.ReadOnly, r["specialty.dental"].State);
    }

    [Fact]
    public void Pack_without_active_parent_is_inactive()
    {
        var r = Resolve(Set("specialty.dental"));
        Assert.Equal(ActivationState.Inactive, r["specialty.dental"].State);
        Assert.Contains(r["specialty.dental"].Reasons, x => x.Code == "DEPENDENCY_INACTIVE");
    }

    [Fact]
    public void Enabling_a_pack_plans_its_parent_too()
    {
        var plan = TenantActivation.PlanEnable(Package, Set(), AllLicensed, "specialty.dental");
        Assert.True(plan.Allowed);
        Assert.Equal(["clinicos.core", "specialty.dental"], plan.ModulesToEnable);
    }

    [Fact]
    public void Enabling_never_grants_licences()
    {
        var plan = TenantActivation.PlanEnable(Package, Set(), _ => LicenseState.None, "specialty.dental");
        Assert.False(plan.Allowed);
        Assert.Contains(plan.Problems, p => p.Code == "NOT_ENTITLED");
    }

    [Fact]
    public void Platform_and_unknown_modules_cannot_be_enabled()
    {
        Assert.Contains(TenantActivation.PlanEnable(Package, Set(), AllLicensed, "core.documents").Problems, p => p.Code == "PLATFORM_MODULE");
        Assert.Contains(TenantActivation.PlanEnable(Package, Set(), AllLicensed, "nope.module").Problems, p => p.Code == "UNKNOWN_MODULE");
    }

    [Fact]
    public void Incompatible_combinations_are_refused_and_reported()
    {
        var opd = Product("hospitalos.opd") with { IncompatibleWith = ["clinicos.core"] };
        ModuleManifest[] package = [Core, Clinic, opd];

        var plan = TenantActivation.PlanEnable(package, Set("clinicos.core"), AllLicensed, "hospitalos.opd");
        Assert.False(plan.Allowed);
        Assert.Contains(plan.Problems, p => p.Code == "INCOMPATIBLE_COMBINATION");

        var both = TenantActivation.Resolve(package, Set("clinicos.core", "hospitalos.opd"), AllLicensed).ToDictionary(a => a.Id);
        Assert.Equal(ActivationState.Inactive, both["clinicos.core"].State);
        Assert.Equal(ActivationState.Inactive, both["hospitalos.opd"].State);
    }

    [Fact]
    public void Dependents_are_found_transitively()
    {
        var addon = Make("specialty.dental-imaging", ModuleKind.Pack, schema: "specialty_dental_imaging", deps: [("specialty.dental", "^1.0.0")]);
        ModuleManifest[] package = [Core, Clinic, Dental, addon];
        var dependents = TenantActivation.EnabledDependents(package, Set("clinicos.core", "specialty.dental", "specialty.dental-imaging"), "clinicos.core");
        Assert.Equal(["specialty.dental", "specialty.dental-imaging"], dependents);
    }
}
