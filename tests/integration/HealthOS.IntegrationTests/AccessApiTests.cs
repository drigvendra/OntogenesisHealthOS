using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>
/// M2 gate evidence through the HTTP API: platform/tenant permission rules, MFA requirements,
/// tenant isolation, membership re-validation and suspension.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AccessApiTests(PostgresFixture db) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ApiHost(db);
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

    private async Task<Guid> NewOperatorAsync()
    {
        var (id, email) = await NewUserAsync("operator");
        await using var sp = db.BuildServices();
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(email, Ct));
        return id;
    }

    private async Task<TenantSummary> CreateTenantAsync(Guid operatorId, string prefix)
    {
        var code = prefix + Guid.NewGuid().ToString("N")[..10];
        var r = await _api.As(operatorId).PostAsJsonAsync("/api/platform/tenants", new CreateTenantRequest(code, "Synthetic " + prefix), Ct);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.Read<TenantSummary>();
    }

    private async Task<Guid> NewTenantAdminAsync(Guid operatorId, Guid tenantId)
    {
        var (id, email) = await NewUserAsync("admin");
        var r = await _api.As(operatorId).PostAsJsonAsync($"/api/platform/tenants/{tenantId}/admins", new { email }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        return id;
    }

    [Fact]
    public async Task Platform_actions_require_operator_and_mfa()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var (nobody, _) = await NewUserAsync("nobody");
        var body = new CreateTenantRequest("x" + Guid.NewGuid().ToString("N")[..10], "Synthetic");

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op, mfa: false).PostAsJsonAsync("/api/platform/tenants", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(nobody, mfa: true).PostAsJsonAsync("/api/platform/tenants", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _api.As(op, mfa: true).PostAsJsonAsync("/api/platform/tenants", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _api.As(op, mfa: true).PostAsJsonAsync("/api/platform/tenants", body, Ct)).StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_works_in_own_tenant_and_sees_nothing_of_another()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var a = await CreateTenantAsync(op, "a");
        var b = await CreateTenantAsync(op, "b");
        var adminA = await NewTenantAdminAsync(op, a.Id);
        var adminB = await NewTenantAdminAsync(op, b.Id);

        var orgA = await (await _api.As(adminA, a.Id).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Org A"), Ct)).Read<OrganizationDto>();
        var orgB = await (await _api.As(adminB, b.Id).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Org B"), Ct)).Read<OrganizationDto>();

        var seenByA = await (await _api.As(adminA, a.Id).GetAsync("/api/core/tenancy/organizations", Ct)).Read<List<OrganizationDto>>();
        Assert.Contains(seenByA, o => o.Id == orgA.Id);
        Assert.DoesNotContain(seenByA, o => o.Id == orgB.Id);

        // Admin A presenting tenant B in the session (not a member): no tenant context, so denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(adminA, b.Id).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);

        // Admin A cannot create a site under tenant B's organization (invisible through RLS).
        var site = await _api.As(adminA, a.Id).PostAsJsonAsync("/api/core/tenancy/sites", new CreateSiteRequest(orgB.Id, "S1", "Site", "Asia/Kolkata"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, site.StatusCode);
    }

    [Fact]
    public async Task Mfa_is_required_for_sensitive_tenant_actions_only()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var t = await CreateTenantAsync(op, "m");
        var admin = await NewTenantAdminAsync(op, t.Id);

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t.Id, mfa: false).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(admin, t.Id, mfa: false).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("X"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Suspending_a_tenant_takes_effect_immediately()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var t = await CreateTenantAsync(op, "s");
        var admin = await NewTenantAdminAsync(op, t.Id);

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t.Id).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(op).PostAsync($"/api/platform/tenants/{t.Id}/suspend", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(admin, t.Id).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
    }

    [Fact]
    public async Task Tenant_roles_cannot_contain_platform_permissions()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var t = await CreateTenantAsync(op, "r");
        var admin = await NewTenantAdminAsync(op, t.Id);

        var escalate = await _api.As(admin, t.Id).PostAsJsonAsync("/api/core/identity/roles",
            new { code = "sneaky", name = "Sneaky", permissions = new[] { "core.tenancy.tenants.manage" } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, escalate.StatusCode);

        var ok = await _api.As(admin, t.Id).PostAsJsonAsync("/api/core/identity/roles",
            new { code = "viewer", name = "Viewer", permissions = new[] { "core.tenancy.structure.read" } }, Ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Members_without_roles_have_no_permissions_and_operators_have_no_tenant_access()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var t = await CreateTenantAsync(op, "p");
        var admin = await NewTenantAdminAsync(op, t.Id);
        var (member, email) = await NewUserAsync("member");

        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t.Id).PostAsJsonAsync("/api/core/identity/members", new { email }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(member, t.Id).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);

        // A platform operator is not implicitly a member of any tenant.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op, t.Id).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
    }

    [Fact]
    public async Task Me_reports_active_tenant_and_operator_flag()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var t = await CreateTenantAsync(op, "w");
        var admin = await NewTenantAdminAsync(op, t.Id);

        var me = await (await _api.As(admin, t.Id).GetAsync("/api/core/identity/me", Ct)).Read<CurrentUser>();
        Assert.Equal(admin, me.UserId);
        Assert.Equal(t.Id, me.ActiveTenantId);
        Assert.False(me.IsPlatformOperator);

        var tenants = await (await _api.As(admin).GetAsync("/api/core/identity/me/tenants", Ct)).Read<List<TenantSummary>>();
        Assert.Equal([t.Id], tenants.Select(x => x.Id));

        Assert.True((await (await _api.As(op).GetAsync("/api/core/identity/me", Ct)).Read<CurrentUser>()).IsPlatformOperator);
    }
}
