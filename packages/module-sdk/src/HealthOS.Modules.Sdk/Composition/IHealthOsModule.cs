using HealthOS.Modules.Sdk.Manifest;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Modules.Sdk.Composition;

/// <summary>
/// Entry point of a build-time packaged module. Implementations live in the module's internal
/// project and are referenced only by the host composition root.
/// </summary>
public interface IHealthOsModule
{
    ModuleManifest Manifest { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

/// <summary>Base class that loads the manifest embedded in the module assembly.</summary>
public abstract class HealthOsModule : IHealthOsModule
{
    private readonly Lazy<ModuleManifest> _manifest;

    protected HealthOsModule()
    {
        _manifest = new Lazy<ModuleManifest>(() => ModuleManifestReader.FromAssembly(GetType().Assembly));
    }

    public ModuleManifest Manifest => _manifest.Value;

    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
