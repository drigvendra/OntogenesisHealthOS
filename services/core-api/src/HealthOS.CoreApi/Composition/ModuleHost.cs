using System.Reflection;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;

namespace HealthOS.CoreApi.Composition;

public static class PlatformInfo
{
    /// <summary>Version of the HealthOS Core platform that module manifests are checked against.</summary>
    public const string CoreVersion = "0.1.0";
}

public static class InstalledModules
{
    /// <summary>
    /// Modules compiled into this package: every IHealthOsModule in the HealthOS.* assemblies shipped next
    /// to the host. Which assemblies ship is decided by the bundle profile at build time
    /// (deploy/bundles/*.props), so the package contents — not code — define the product bundle.
    /// Test assemblies are never scanned.
    /// </summary>
    public static IReadOnlyList<IHealthOsModule> Create()
    {
        var dir = Path.GetDirectoryName(typeof(InstalledModules).Assembly.Location) ?? AppContext.BaseDirectory;
        var files = Directory.GetFiles(dir, "HealthOS.*.dll")
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("Tests", StringComparison.Ordinal));

        return files
            .Select(f => Assembly.Load(AssemblyName.GetAssemblyName(f)))
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => typeof(IHealthOsModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false } && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IHealthOsModule)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Manifest.Id, StringComparer.Ordinal)
            .ToList();
    }
}

public static class ModuleHostExtensions
{
    /// <summary>
    /// Resolves the package (fails startup on any manifest/dependency issue), then registers
    /// module services in dependency order and the permission catalog of all installed modules.
    /// </summary>
    public static IReadOnlyList<IHealthOsModule> AddHealthOsModules(
        this WebApplicationBuilder builder,
        IReadOnlyList<IHealthOsModule> modules)
    {
        var resolution = PackageResolver.Resolve(modules.Select(m => m.Manifest), PlatformInfo.CoreVersion);
        if (!resolution.Succeeded)
        {
            throw new InvalidOperationException(
                "Module package resolution failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, resolution.Issues.Select(i => "  " + i)));
        }

        var byId = modules.ToDictionary(m => m.Manifest.Id, StringComparer.Ordinal);
        var ordered = resolution.OrderedModules.Select(m => byId[m.Id]).ToList();

        foreach (var module in ordered)
        {
            module.ConfigureServices(builder.Services, builder.Configuration);
        }

        builder.Services.AddSingleton<IReadOnlyList<IHealthOsModule>>(ordered);
        builder.Services.AddSingleton(new PermissionCatalog(ordered.Select(m => m.Manifest)));
        return ordered;
    }

    public static void MapHealthOsModules(this WebApplication app)
    {
        foreach (var module in app.Services.GetRequiredService<IReadOnlyList<IHealthOsModule>>())
        {
            // Every endpoint a module maps carries its owner, which the module gate reads.
            var group = app.MapGroup(string.Empty).WithMetadata(new ModuleOwner(module.Manifest));
            module.MapEndpoints(group);
        }
    }
}
