using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>A pack on top of <see cref="TestLicensedModule"/>, used to prove dependency rules.</summary>
public sealed class TestAddonModule : IHealthOsModule
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "test.addon",
        Kind = ModuleKind.Pack,
        Version = "0.1.0",
        DisplayName = "Test add-on pack",
        RequiresCore = "^0.1.0",
        Dependencies = [new ModuleDependency("test.licensed", "^0.1.0")],
        License = new ModuleLicense("TESTPROD", "addon"),
        Permissions = [new PermissionDeclaration("test.addon.read", "Read add-on data")],
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/test/addon/ping", () => TypedResults.Ok("addon")).RequirePermission("test.addon.read");
}

/// <summary>A minimal licensed product module used only to prove the entitlement gate.</summary>
public sealed class TestLicensedModule : IHealthOsModule
{
    public const string Feature = "TESTPROD:core";

    public ModuleManifest Manifest { get; } = new()
    {
        Id = "test.licensed",
        Kind = ModuleKind.Product,
        Version = "0.1.0",
        DisplayName = "Test licensed product",
        RequiresCore = "^0.1.0",
        License = new ModuleLicense("TESTPROD", "core"),
        Permissions =
        [
            new PermissionDeclaration("test.licensed.read", "Read test data"),
            new PermissionDeclaration("test.licensed.write", "Write test data"),
        ],
        Ui = new ModuleUi { Routes = [new UiRoute("/test/licensed", "admin-web", "test.licensed.read")] },
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/test/licensed");
        g.MapGet("/ping", () => TypedResults.Ok("pong")).RequirePermission("test.licensed.read");
        g.MapPost("/ping", () => TypedResults.Ok("written")).RequirePermission("test.licensed.write");
    }
}
