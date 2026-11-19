using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HealthOS.MedTechOS.Assets;

/// <summary>
/// MedTechOS: manifest, licence feature, permissions and navigation are real and enforced by the
/// platform. Functional scope is delivered in milestone M11; until then the module only reports
/// its own status (no placeholder features).
/// </summary>
public sealed class MedTechAssetsModule : HealthOsModule
{
    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/medtechos/status", () => TypedResults.Ok(new
            {
                module = Manifest.Id,
                version = Manifest.Version,
                status = "skeleton",
                functionalScopeMilestone = "M11",
            }))
            .RequirePermission("medtechos.assets.read");
    }
}