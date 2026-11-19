using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthOS.CoreApi.Tests;

/// <summary>
/// In-process host. The database points at a closed port (no Docker needed); authentication is a
/// header-driven test scheme so tests do not need Keycloak.
/// </summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    public const string UserHeader = "X-Test-User";
    public const string AmrHeader = "X-Test-Amr";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2",
        ["Authentication:Oidc:Authority"] = "https://idp.example.invalid/realms/test",
        ["Authentication:Oidc:ClientId"] = "test-client",
        ["Authentication:Oidc:ClientSecret"] = "test-secret",
    };

    public TestHost With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (k, v) in _settings)
        {
            builder.UseSetting(k, v);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
            // Only authentication is replaced; challenge/forbid keep the production behaviour (cookie
            // handler returning 401/403 for APIs), so tests exercise the real API responses.
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = HeaderAuthHandler.SchemeName);
        });
    }

    public HttpClient ApiClient(string? user = null, string? amr = null, bool csrf = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (csrf)
        {
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
        }

        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(UserHeader, user);
        }

        if (amr is not null)
        {
            client.DefaultRequestHeaders.Add(AmrHeader, amr);
        }

        return client;
    }
}

internal sealed class HeaderAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestHeader";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestHost.UserHeader, out var user) || string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user!), new("name", $"Synthetic {user}") };
        foreach (var amr in Request.Headers[TestHost.AmrHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            claims.Add(new Claim("amr", amr));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, "name", "role"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
