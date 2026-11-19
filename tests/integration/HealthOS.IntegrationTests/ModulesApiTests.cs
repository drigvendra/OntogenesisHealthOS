using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Audit;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Modules;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>M3 gate evidence: per-tenant enable/disable with dependency and licence rules; disabling keeps data.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class ModulesApiTests(PostgresFixture db) : IAsyncLifetime
{
    private ApiHost _api = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = new ApiHost(db, new TestLicensedModule(), new TestAddonModule());
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

    private async Task<(Guid Operator, Guid Tenant, Guid Admin)> SetupAsync()
    {
        var (op, opEmail) = await NewUserAsync("operator");
        await using (var sp = db.BuildServices())
        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
            await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(opEmail, Ct);
        }

        var tenant = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest("m" + Guid.NewGuid().ToString("N")[..10], "Synthetic modules"), Ct)).Read<TenantSummary>();
        var (admin, adminEmail) = await NewUserAsync("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/admins", new { email = adminEmail }, Ct)).StatusCode);
        return (op, tenant.Id, admin);
    }

    private Task License(Guid op, Guid tenant, string feature) =>
        _api.As(op).PutAsJsonAsync($"/api/platform/tenants/{tenant}/entitlements", new { feature }, Ct);

    private Task<HttpResponseMessage> Enable(Guid admin, Guid tenant, string module, bool includeDependencies = false) =>
        _api.As(admin, tenant).PostAsJsonAsync($"/api/core/modules/{module}/enable", new EnableModuleRequest(includeDependencies), Ct);

    [Fact]
    public async Task Modules_list_explains_why_a_module_is_inactive()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin) = await SetupAsync();

        var modules = await (await _api.As(admin, t).GetAsync("/api/core/modules", Ct)).Read<List<ModuleStatusDto>>();
        var licensed = Assert.Single(modules, m => m.Id == "test.licensed");
        Assert.Equal("Inactive", licensed.State);
        Assert.Contains(licensed.Reasons, r => r.Code == "NOT_ENABLED");
        Assert.Contains(licensed.Reasons, r => r.Code == "NOT_ENTITLED");
        Assert.All(modules.Where(m => m.Kind == "platform"), m => Assert.Equal("Active", m.State));
    }

    [Fact]
    public async Task Enabling_requires_a_licence()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupAsync();

        var refused = await Enable(admin, t, "test.licensed");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains((await refused.Content.ReadFromJsonAsync<ActivationProblem>(Ct))!.Reasons, r => r.Code == "NOT_ENTITLED");

        await License(op, t, TestLicensedModule.Feature);
        Assert.Equal(HttpStatusCode.OK, (await Enable(admin, t, "test.licensed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
    }

    [Fact]
    public async Task Licensed_but_not_enabled_module_is_not_routable()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupAsync();
        await License(op, t, TestLicensedModule.Feature);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
    }

    [Fact]
    public async Task Dependencies_are_enabled_only_when_asked_and_block_disabling()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupAsync();
        await License(op, t, TestLicensedModule.Feature);
        await License(op, t, "TESTPROD:addon");

        var needsParent = await Enable(admin, t, "test.addon");
        Assert.Equal(HttpStatusCode.Conflict, needsParent.StatusCode);
        Assert.Equal(["test.licensed", "test.addon"], (await needsParent.Content.ReadFromJsonAsync<ActivationProblem>(Ct))!.Required);

        Assert.Equal(HttpStatusCode.OK, (await Enable(admin, t, "test.addon", includeDependencies: true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).GetAsync("/api/test/addon/ping", Ct)).StatusCode);

        var blocked = await _api.As(admin, t).PostAsync("/api/core/modules/test.licensed/disable", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(["test.addon"], (await blocked.Content.ReadFromJsonAsync<ActivationProblem>(Ct))!.Required);
    }

    [Fact]
    public async Task Disabling_hides_the_module_and_re_enabling_restores_it()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupAsync();
        await License(op, t, TestLicensedModule.Feature);
        await Enable(admin, t, "test.licensed");

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).PostAsync("/api/core/modules/test.licensed/disable", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Enable(admin, t, "test.licensed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);

        var events = await (await _api.As(admin, t).GetAsync("/api/core/audit/events", Ct)).Read<List<AuditEventDto>>();
        Assert.Contains(events, e => e.Action == "core.modules.module.disabled" && e.Details.Contains("\"dataRetained\":true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Platform_modules_cannot_be_disabled_and_activation_needs_mfa()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin) = await SetupAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.As(admin, t).PostAsync("/api/core/modules/core.audit/disable", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(admin, t, mfa: false).PostAsJsonAsync("/api/core/modules/test.licensed/enable", new EnableModuleRequest(false), Ct)).StatusCode);
    }

    [Fact]
    public async Task Operators_can_enable_modules_during_onboarding()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, t, admin) = await SetupAsync();
        await License(op, t, TestLicensedModule.Feature);

        var r = await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{t}/modules/test.licensed/enable", new EnableModuleRequest(false), Ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).GetAsync("/api/test/licensed/ping", Ct)).StatusCode);
    }
}
