using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using HealthOS.Core.Identity.Contracts;
using HealthOS.CoreApi.Security;
using HealthOS.Modules.Sdk.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HealthOS.CoreApi.Tests;

public sealed class HostTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;

    public HostTests(TestHost host) => _host = host;

    [Fact]
    public async Task Liveness_is_ok_without_dependencies()
    {
        var r = await _host.ApiClient().GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Readiness_fails_when_database_is_unreachable()
    {
        var r = await _host.ApiClient().GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"postgres\":\"Unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal); // no connection details leak
    }

    [Fact]
    public void Every_endpoint_declares_exactly_one_access_rule()
    {
        var endpoints = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        var offenders = endpoints
            .Where(e => e.Metadata.GetOrderedMetadata<EndpointAccess>().Count != 1)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(offenders.Count == 0, "Endpoints without exactly one access declaration: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task Api_requires_csrf_header()
    {
        var r = await _host.ApiClient(user: "u1", csrf: false)
            .GetAsync(new Uri("/api/core/identity/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Api_returns_401_for_anonymous_callers()
    {
        var r = await _host.ApiClient().GetAsync(new Uri("/api/core/identity/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Api_never_redirects_anonymous_callers_to_the_identity_provider()
    {
        var r = await _host.ApiClient().GetAsync(new Uri("/api/core/identity/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Null(r.Headers.Location);
        Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_authenticated_identity()
    {
        var me = await _host.ApiClient(user: "synthetic-user-1", amr: "pwd,otp")
            .GetFromJsonAsync<CurrentUser>(new Uri("/api/core/identity/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal("synthetic-user-1", me.SubjectId);
        Assert.True(me.MultiFactorAuthenticated);
    }

    [Fact]
    public async Task Password_only_login_is_not_reported_as_mfa()
    {
        var me = await _host.ApiClient(user: "synthetic-user-2", amr: "pwd")
            .GetFromJsonAsync<CurrentUser>(new Uri("/api/core/identity/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.False(me!.MultiFactorAuthenticated);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var r = await _host.ApiClient().GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", r.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permission_policies_deny_until_m2_grants_exist()
    {
        var auth = _host.Services.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));

        var result = await auth.AuthorizeAsync(user, null, EndpointAccessExtensions.PermissionPolicyPrefix + "careops.document.read");

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/quality/documents?x=1", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Login_return_url_must_be_local(string? url, bool expected) =>
        Assert.Equal(expected, BffAuthentication.IsLocalUrl(url));
}

public sealed class StartupValidationTests
{
    [Fact]
    public void Startup_fails_fast_when_oidc_secret_is_missing()
    {
        using var host = new TestHost().With("Authentication:Oidc:ClientSecret", "");
        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains(ex.Flatten(), e => e is OptionsValidationException);
    }

    [Fact]
    public void Startup_fails_fast_when_database_is_not_configured()
    {
        using var host = new TestHost().With("Database:ConnectionString", "");
        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains(ex.Flatten(), e => e is OptionsValidationException);
    }
}

internal static class ExceptionExtensions
{
    public static IEnumerable<Exception> Flatten(this Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
