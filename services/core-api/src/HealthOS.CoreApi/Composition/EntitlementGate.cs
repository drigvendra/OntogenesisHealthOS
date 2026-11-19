using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Manifest;
using HealthOS.Modules.Sdk.Security;

namespace HealthOS.CoreApi.Composition;

/// <summary>Endpoint metadata naming the module that mapped the endpoint (added by the host).</summary>
public sealed record ModuleOwner(ModuleManifest Manifest);

/// <summary>
/// Tenant-level module activation gate (enabled ∧ licensed ∧ dependencies active ∧ compatible):
/// - inactive: 404, as if the module were not installed (its data is retained);
/// - read-only (expired licence): safe methods only, writes 403;
/// - active: pass through.
/// Platform modules and platform-scope requests are not gated.
/// </summary>
public sealed class ModuleGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IRequestContext ctx)
    {
        var owner = http.GetEndpoint()?.Metadata.GetMetadata<ModuleOwner>()?.Manifest;
        if (owner is null || owner.Kind == ModuleKind.Platform || ctx.IsPlatformScope)
        {
            await next(http).ConfigureAwait(false);
            return;
        }

        var activation = await http.RequestServices.GetRequiredService<IModuleActivation>().GetAsync(owner.Id, http.RequestAborted).ConfigureAwait(false);
        switch (activation?.State ?? ActivationState.Inactive)
        {
            case ActivationState.Inactive:
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            case ActivationState.ReadOnly when !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method):
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { title = "This module's licence has expired; data is read-only." }).ConfigureAwait(false);
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
    /// What the signed-in user may see in a web app: routes of modules that are active for the
    /// tenant, filtered by the user's permissions. UIs must build menus only from this.
    /// </summary>
    public static void MapNavigation(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/core/navigation", async (HttpContext http, IModuleActivation activation, IPermissionEvaluator permissions, string? appName) =>
            {
                var items = new List<NavigationItem>();
                foreach (var a in await activation.GetAllAsync(http.RequestAborted))
                {
                    if (a.State == ActivationState.Inactive)
                    {
                        continue;
                    }

                    foreach (var route in a.Manifest.Ui.Routes.Where(r => appName is null || r.App == appName))
                    {
                        if (route.Permission is null ||
                            (await permissions.EvaluateAsync(http.User, route.Permission, http.RequestAborted)).Granted)
                        {
                            items.Add(new NavigationItem(a.Id, a.Manifest.DisplayName, route.App, route.Path, a.State == ActivationState.ReadOnly));
                        }
                    }
                }

                return TypedResults.Ok(items);
            })
            .RequireAuthenticatedUser("Navigation is personal: it only lists what this user may open in the active tenant.");
    }
}
