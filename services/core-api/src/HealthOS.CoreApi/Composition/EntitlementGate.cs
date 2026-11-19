using HealthOS.Core.Licensing.Contracts;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;

namespace HealthOS.CoreApi.Composition;

/// <summary>Endpoint metadata naming the module that mapped the endpoint (added by the host).</summary>
public sealed record ModuleOwner(ModuleManifest Manifest);

/// <summary>
/// Tenant-level module activation for licensed modules:
/// - not entitled: 404, as if the module were not installed;
/// - read-only (expired / grace): safe methods only, writes 403;
/// - active: pass through.
/// Platform modules and platform-scope requests are not gated.
/// </summary>
public sealed class EntitlementGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IRequestContext ctx)
    {
        var license = http.GetEndpoint()?.Metadata.GetMetadata<ModuleOwner>()?.Manifest.License;
        if (license is null || ctx.IsPlatformScope)
        {
            await next(http).ConfigureAwait(false);
            return;
        }

        var entitlements = http.RequestServices.GetRequiredService<IEntitlements>();
        var status = await entitlements.GetAsync(LicenseFeatures.Key(license.ProductCode, license.Feature), http.RequestAborted).ConfigureAwait(false);
        switch (status.State)
        {
            case EntitlementState.None:
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            case EntitlementState.ReadOnly when !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method):
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { title = "This feature's license has expired; data is read-only." }).ConfigureAwait(false);
                return;
            default:
                await next(http).ConfigureAwait(false);
                return;
        }
    }
}

public sealed record NavigationItem(string ModuleId, string ModuleName, string App, string Path, bool ReadOnly);

public static class NavigationEndpoints
{
    /// <summary>
    /// What the signed-in user may see in a web app: routes of modules that are installed and entitled
    /// for the active tenant, filtered by the user's permissions. UIs must build menus only from this.
    /// </summary>
    public static void MapNavigation(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/core/navigation", async (HttpContext http, IReadOnlyList<IHealthOsModule> modules, IPermissionEvaluator permissions, IEntitlements entitlements, string? appName) =>
            {
                var items = new List<NavigationItem>();
                foreach (var m in modules)
                {
                    var readOnly = false;
                    if (m.Manifest.License is { } l)
                    {
                        var status = await entitlements.GetAsync(LicenseFeatures.Key(l.ProductCode, l.Feature), http.RequestAborted);
                        if (status.State == EntitlementState.None)
                        {
                            continue;
                        }

                        readOnly = status.State == EntitlementState.ReadOnly;
                    }

                    foreach (var route in m.Manifest.Ui.Routes.Where(r => appName is null || r.App == appName))
                    {
                        if (route.Permission is null ||
                            (await permissions.EvaluateAsync(http.User, route.Permission, http.RequestAborted)).Granted)
                        {
                            items.Add(new NavigationItem(m.Manifest.Id, m.Manifest.DisplayName, route.App, route.Path, readOnly));
                        }
                    }
                }

                return TypedResults.Ok(items);
            })
            .RequireAuthenticatedUser("Navigation is personal: it only lists what this user may open in the active tenant.");
    }
}
