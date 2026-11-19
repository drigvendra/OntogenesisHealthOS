using System.ComponentModel.DataAnnotations;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace HealthOS.CoreApi.Security;

public sealed class OidcSettings
{
    public const string Section = "Authentication:Oidc";

    [Required, Url]
    public string Authority { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Only false for local development against http://localhost Keycloak.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

/// <summary>
/// Backend-for-frontend authentication: the SPA never sees OAuth tokens. core-api runs the OIDC
/// authorization-code + PKCE flow and issues an HttpOnly session cookie.
/// </summary>
public static class BffAuthentication
{
    public const string OidcScheme = OpenIdConnectDefaults.AuthenticationScheme;
    public const string CsrfHeader = "X-CSRF";

    public static IServiceCollection AddBffAuthentication(this IServiceCollection services, IHostEnvironment env)
    {
        services.AddOptions<OidcSettings>()
            .BindConfiguration(OidcSettings.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OidcScheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = env.IsDevelopment() ? "healthos.session" : "__Host-healthos";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = true;
                // APIs return status codes instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => StatusOrRedirect(ctx, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => StatusOrRedirect(ctx, StatusCodes.Status403Forbidden);
            })
            .AddOpenIdConnect(OidcScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>(OidcScheme)
            .Configure<IOptions<OidcSettings>>((o, settings) =>
            {
                var s = settings.Value;
                o.Authority = s.Authority;
                o.ClientId = s.ClientId;
                o.ClientSecret = s.ClientSecret;
                o.RequireHttpsMetadata = s.RequireHttpsMetadata;
                o.ResponseType = "code";
                o.UsePkce = true;
                o.SaveTokens = true;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = true;
                o.CallbackPath = "/bff/signin-oidc";
                o.SignedOutCallbackPath = "/bff/signout-callback-oidc";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.ClaimActions.Remove("acr");
                o.TokenValidationParameters.NameClaimType = "name";
                o.Events.OnTicketReceived = ProvisionUserAsync;
            });

        return services;
    }

    /// <summary>
    /// Just-in-time provisioning: map the IdP subject to an internal user id and store it in the
    /// session so modules never depend on IdP-specific identifiers.
    /// </summary>
    internal static async Task ProvisionUserAsync(TicketReceivedContext ctx)
    {
        var principal = ctx.Principal;
        var subject = principal?.FindFirst("sub")?.Value;
        var issuer = principal?.FindFirst("iss")?.Value ?? principal?.FindFirst("sub")?.Issuer;
        if (principal?.Identity is not System.Security.Claims.ClaimsIdentity identity || subject is null || issuer is null)
        {
            ctx.Fail("Identity provider did not supply a subject.");
            return;
        }

        var directory = ctx.HttpContext.RequestServices.GetRequiredService<HealthOS.Core.Identity.Contracts.IUserDirectory>();
        var userId = await directory.EnsureUserAsync(
            new HealthOS.Core.Identity.Contracts.ExternalIdentity(issuer, subject, principal.FindFirst("name")?.Value, principal.FindFirst("email")?.Value),
            ctx.HttpContext.RequestAborted).ConfigureAwait(false);

        identity.AddClaim(new System.Security.Claims.Claim(HealthOS.Core.Identity.Contracts.HealthOsClaimTypes.UserId, userId.ToString()));
    }

    private static Task StatusOrRedirect(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (IsApiRequest(ctx.Request))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    internal static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    public static void MapBffEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff").WithTags("bff");

        bff.MapGet("/login", (string? returnUrl) =>
            {
                var target = IsLocalUrl(returnUrl) ? returnUrl! : "/";
                return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [OidcScheme]);
            })
            .AllowPublic("Starts the OIDC login flow; anonymous by definition.");

        bff.MapPost("/logout", () => Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme]))
            .RequireAuthenticatedUser("Any signed-in user may end their own session.");
    }

    /// <summary>Prevents open redirects: only app-relative paths are accepted.</summary>
    internal static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}

/// <summary>
/// CSRF defense for cookie-authenticated APIs: every /api and /bff/logout request must carry
/// "X-CSRF: 1". Browsers cannot add custom headers cross-origin without a CORS preflight, which we never allow.
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var protectedPath = BffAuthentication.IsApiRequest(context.Request) ||
                            (path.StartsWithSegments("/bff/logout", StringComparison.OrdinalIgnoreCase));

        if (protectedPath && context.Request.Headers[BffAuthentication.CsrfHeader] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { title = "Missing anti-forgery header." });
        }

        return next(context);
    }
}
