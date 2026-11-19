using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HealthOS.CareOps.Quality;

/// <summary>
/// CareOps Quality: manifest, licence feature, permissions and navigation are real and enforced by the
/// platform. Functional scope is delivered in milestone M5; until then the module only reports
/// its own status (no placeholder features).
/// </summary>
public sealed class CareOpsQualityModule : HealthOsModule
{
    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/careops/status", () => TypedResults.Ok(new
            {
                module = Manifest.Id,
                version = Manifest.Version,
                status = "skeleton",
                functionalScopeMilestone = "M5",
            }))
            .RequirePermission("careops.documents.read");
    }
}