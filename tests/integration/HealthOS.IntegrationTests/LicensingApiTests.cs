using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Audit;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.CoreApi.Composition;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>M2 gate evidence: unentitled modules are not routable and not in navigation; expiry is read-only.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class LicensingApiTests(PostgresFixture db) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ApiHost(db, new TestLicensedModule());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<(Guid Id, string Email)> NewUserAsync(string label)
    {
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        var email = $"{label}-{Guid.NewGuid():N}@example.invalid";
        var id = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(new ExternalIdentity("https://idp.test", label + "-" + Guid.NewGuid(), "Synthetic " + label, email), Ct);
        return (id, email);
    }

    private async Task<(Guid Operator, Guid Tenant, Guid Admin)> SetupTenantAsync(string prefix)
    {
        var (op, opEmail) = await NewUserAsync("operator");
        await using (var sp = db.BuildServices())
        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
            await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(opEmail, Ct);
        }

        var tenant = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest(prefix + Guid.NewGuid().ToString("N")[..10], "Synthetic " + prefix), Ct)).Read<TenantSummary>();
        var (admin, adminEmail) = await NewUserAsync("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/admins", new { email = adminEmail }, Ct)).StatusCode);
        return (op, tenant.Id, admin);
    }

    /// <summary>Grants the licence and enables the module (both are needed for activation).</summary>
    private async Task<HttpResponseMessage> Grant(Guid op, Guid tenant, DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null, int? graceDays = null)
    {
        var r = await _api.As(op).PutAsJsonAsync($"/api/platform/tenants/{tenant}/entitlements",
            new { feature = TestLicensedModule.Feature, validFrom, validTo, graceDays }, Ct);
        if (r.IsSuccessStatusCode)
        {
            var enable = await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant}/modules/test.licensed/enable", new { includeDependencies = false }, Ct);
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        }

        return r;
    }

    private async Task<List<NavigationItem>> Navigation(Guid user, Guid tenant) =>
        await (await _api.As(user, tenant).GetAsync("/api/core/navigation", Ct)).Read<List<NavigationItem>>();

    [Fact]
    public async Task Unentitled_module_is_not_routable_and_not_in_navigation()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin) = await SetupTenantAsync("la");

        Assert.Equal(HttpStatusCode.NotFound, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
        Assert.DoesNotContain(await Navigation(admin, t), n => n.ModuleId == "test.licensed");
    }

    [Fact]
    public async Task Entitled_module_is_routable_and_in_navigation_for_that_tenant_only()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, a, adminA) = await SetupTenantAsync("lb");
        var (_, b, adminB) = await SetupTenantAsync("lc");

        Assert.Equal(HttpStatusCode.OK, (await Grant(op, a)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _api.As(adminA, a).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(adminA, a).PostAsync("/api/test/licensed/ping", null, Ct)).StatusCode);
        var nav = Assert.Single(await Navigation(adminA, a), n => n.ModuleId == "test.licensed");
        Assert.False(nav.ReadOnly);

        Assert.Equal(HttpStatusCode.NotFound, (await _api.As(adminB, b).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expired_entitlement_is_read_only_never_gone()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupTenantAsync("ld");
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, (await Grant(op, t, now.AddDays(-60), now.AddDays(-1), graceDays: 30)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(admin, t).PostAsync("/api/test/licensed/ping", null, Ct)).StatusCode);
        Assert.True(Assert.Single(await Navigation(admin, t), n => n.ModuleId == "test.licensed").ReadOnly);
    }

    [Fact]
    public async Task Navigation_respects_permissions()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupTenantAsync("le");
        await Grant(op, t);
        var (member, email) = await NewUserAsync("member");
        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t).PostAsJsonAsync("/api/core/identity/members", new { email }, Ct)).StatusCode);

        Assert.DoesNotContain(await Navigation(member, t), n => n.ModuleId == "test.licensed");
        Assert.Contains(await Navigation(admin, t), n => n.ModuleId == "test.licensed");
    }

    [Fact]
    public async Task Entitlement_changes_need_operator_mfa_and_are_audited()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupTenantAsync("lf");

        var noMfa = await _api.As(op, mfa: false).PutAsJsonAsync($"/api/platform/tenants/{t}/entitlements", new { feature = TestLicensedModule.Feature }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, noMfa.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(admin, t).PutAsJsonAsync($"/api/platform/tenants/{t}/entitlements", new { feature = TestLicensedModule.Feature }, Ct)).StatusCode);

        var unknown = await _api.As(op).PutAsJsonAsync($"/api/platform/tenants/{t}/entitlements", new { feature = "NOPE:missing" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Grant(op, t)).StatusCode);
        var events = await (await _api.As(op).GetAsync("/api/platform/audit/events?limit=500", Ct)).Read<List<AuditEventDto>>();
        Assert.Contains(events, e => e.Action == "core.licensing.entitlement.set" && e.ResourceId == t.ToString());
    }
}
