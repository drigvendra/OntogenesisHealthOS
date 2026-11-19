using System.Security.Claims;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Core.Identity;

public sealed class CoreIdentityModule : HealthOsModule
{
    // OIDC "amr" values that indicate a second factor (RFC 8176) as emitted by Keycloak and common IdPs.
    private static readonly HashSet<string> MultiFactorMethods = ["mfa", "otp", "hwk", "swk", "sms", "fido", "webauthn"];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<IdentityDbContext>(Manifest.Id, IdentityDbContext.SchemaName, o => new IdentityDbContext(o));
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton(TimeProvider.System);
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/core/identity").WithTags("core.identity");

        group.MapGet("/me", (ClaimsPrincipal user) => TypedResults.Ok(ToCurrentUser(user)))
            .RequireAuthenticatedUser("Every signed-in user may read their own identity.")
            .WithName("GetCurrentUser");
    }

    internal static CurrentUser ToCurrentUser(ClaimsPrincipal user)
    {
        var amr = user.FindAll("amr").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        return new CurrentUser(
            UserId: Guid.TryParse(user.FindFirstValue(HealthOsClaimTypes.UserId), out var id) ? id : null,
            SubjectId: user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            DisplayName: user.FindFirstValue("name") ?? user.Identity?.Name,
            Email: user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email),
            AuthenticationMethods: amr,
            MultiFactorAuthenticated: amr.Any(MultiFactorMethods.Contains));
    }
}
