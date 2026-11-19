using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HealthOS.Specialty.Dental;

/// <summary>
/// Dental pack: manifest, licence feature, permissions and navigation are real and enforced by the
/// platform. Functional scope is delivered in milestone M7; until then the module only reports
/// its own status (no placeholder features).
/// </summary>
public sealed class DentalPackModule : HealthOsModule
{
    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/specialty/dental/status", () => TypedResults.Ok(new
            {
                module = Manifest.Id,
                version = Manifest.Version,
                status = "skeleton",
                functionalScopeMilestone = "M7",
            }))
            .RequirePermission("specialty.dental.chart.read");
    }
}