using HealthOS.Core.Identity;
using HealthOS.Core.Tenancy;
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
    /// Modules compiled into this package. Generated from the bundle profile from M3 onwards;
    /// hand-maintained until then.
    /// </summary>
    public static IReadOnlyList<IHealthOsModule> Create() =>
    [
        new CoreTenancyModule(),
        new CoreIdentityModule(),
    ];
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
            module.MapEndpoints(app);
        }
    }
}
