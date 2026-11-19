using HealthOS.CoreApi.Composition;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Manifest;
using Microsoft.AspNetCore.Builder;

namespace HealthOS.CoreApi.Tests;

public class ModuleHostTests
{
    private sealed class FakeModule(ModuleManifest manifest) : IHealthOsModule
    {
        public ModuleManifest Manifest { get; } = manifest;

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
        }

        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
        }
    }

    [Fact]
    public void Host_refuses_to_start_with_unresolvable_package()
    {
        var orphan = new FakeModule(new ModuleManifest
        {
            Id = "careops.quality",
            Kind = ModuleKind.Product,
            Version = "0.1.0",
            DisplayName = "CareOps",
            RequiresCore = "^0.1.0",
            License = new ModuleLicense("CAREOPS", "quality.core"),
            Dependencies = [new ModuleDependency("core.documents", "^0.1.0")],
        });

        var builder = WebApplication.CreateBuilder();
        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddHealthOsModules([orphan]));
        Assert.Contains("MISSING_DEPENDENCY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_modules_resolve_against_current_core_version()
    {
        var r = PackageResolver.Resolve(InstalledModules.Create().Select(m => m.Manifest), PlatformInfo.CoreVersion);
        Assert.True(r.Succeeded, string.Join("; ", r.Issues));
    }
}
