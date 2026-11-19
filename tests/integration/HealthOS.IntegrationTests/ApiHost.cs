using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthOS.IntegrationTests;

/// <summary>
/// The real core-api pipeline against the test database. Only authentication is substituted: a
/// header scheme stands in for the OIDC session cookie and carries the same claims (hos_uid, hos_tid, amr).
/// Everything after that — request context, membership re-validation, RLS, permissions — is production code.
/// </summary>
public sealed class ApiHost(PostgresFixture db) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", db.AppConnectionString);
        builder.UseSetting("Database:RolePrefix", db.Roles.Prefix);
        builder.UseSetting("Authentication:Oidc:Authority", "https://idp.example.invalid/realms/test");
        builder.UseSetting("Authentication:Oidc:ClientId", "test");
        builder.UseSetting("Authentication:Oidc:ClientSecret", "test");
        builder.UseSetting("Platform:Cell", "TEST-1");
        builder.ConfigureTestServices(s =>
        {
            s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SessionHeaderAuth>(SessionHeaderAuth.Name, _ => { });
            s.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = SessionHeaderAuth.Name);
        });
    }

    public HttpClient As(Guid userId, Guid? tenantId = null, bool mfa = true)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-CSRF", "1");
        c.DefaultRequestHeaders.Add(SessionHeaderAuth.UserHeader, userId.ToString());
        if (tenantId is { } t)
        {
            c.DefaultRequestHeaders.Add(SessionHeaderAuth.TenantHeader, t.ToString());
        }

        c.DefaultRequestHeaders.Add(SessionHeaderAuth.AmrHeader, mfa ? "pwd,otp" : "pwd");
        return c;
    }
}

internal sealed class SessionHeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string Name = "TestSession";
    public const string UserHeader = "X-Test-Uid";
    public const string TenantHeader = "X-Test-Tid";
    public const string AmrHeader = "X-Test-Amr";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var uid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", "synthetic-" + uid), new("name", "Synthetic " + uid), new(HealthOsClaims.UserId, uid!) };
        if (Request.Headers.TryGetValue(TenantHeader, out var tid))
        {
            claims.Add(new Claim(HealthOsClaims.TenantId, tid!));
        }

        claims.AddRange(Request.Headers[AmrHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => new Claim("amr", a)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, "name", "role"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }
}

internal static class HttpExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }
}
