using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Audit;
using HealthOS.Core.Identity;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.IntegrationTests;

/// <summary>Operator access to tenant data only via time-boxed, dual-approved, read-only, tenant-visible break-glass.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class BreakGlassTests(PostgresFixture db) : IAsyncLifetime
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
        await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(email, Ct);
        return id;
    }

    private async Task<(Guid Tenant, Guid Admin)> NewTenantAsync(Guid op)
    {
        var tenant = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest("bg" + Guid.NewGuid().ToString("N")[..10], "Synthetic BG"), Ct)).Read<TenantSummary>();
        var (admin, email) = await NewUserAsync("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/admins", new { email }, Ct)).StatusCode);
        await _api.As(admin, tenant.Id).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Tenant data"), Ct);
        return (tenant.Id, admin);
    }

    private Task<HttpResponseMessage> Request(Guid op, Guid tenant, string reason = "Support ticket HOS-1234: login issue", int minutes = 60) =>
        _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{tenant}/break-glass", new BreakGlassRequest(reason, minutes), Ct);

    [Fact]
    public async Task Access_requires_a_second_operator_and_is_read_only()
    {
        PostgresFixture.SkipIfUnavailable();
        var op1 = await NewOperatorAsync();
        var op2 = await NewOperatorAsync();
        var (tenant, _) = await NewTenantAsync(op1);

        var grant = await (await Request(op1, tenant)).Read<BreakGlassDto>();
        Assert.False(grant.Active);

        // Not yet approved: no access.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op1, tenant).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
        // Self-approval refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op1).PostAsync($"/api/platform/break-glass/{grant.Id}/approve", null, Ct)).StatusCode);
        // Second operator approves.
        Assert.True((await (await _api.As(op2).PostAsync($"/api/platform/break-glass/{grant.Id}/approve", null, Ct)).Read<BreakGlassDto>()).Active);

        Assert.Equal(HttpStatusCode.OK, (await _api.As(op1, tenant).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op1, tenant).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Write attempt"), Ct)).StatusCode);
        // Without MFA even reads are refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op1, tenant, mfa: false).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
    }

    [Fact]
    public async Task Tenant_sees_the_grant_and_its_audit_trail()
    {
        PostgresFixture.SkipIfUnavailable();
        var op1 = await NewOperatorAsync();
        var op2 = await NewOperatorAsync();
        var (tenant, admin) = await NewTenantAsync(op1);

        var grant = await (await Request(op1, tenant)).Read<BreakGlassDto>();
        await _api.As(op2).PostAsync($"/api/platform/break-glass/{grant.Id}/approve", null, Ct);
        await _api.As(op1, tenant).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Denied write"), Ct);

        var grants = await (await _api.As(admin, tenant).GetAsync("/api/core/identity/break-glass", Ct)).Read<List<BreakGlassDto>>();
        Assert.Contains(grants, g => g.Id == grant.Id && g.UserId == op1 && g.ApprovedBy == op2);

        var events = await (await _api.As(admin, tenant).GetAsync("/api/core/audit/events", Ct)).Read<List<AuditEventDto>>();
        Assert.Contains(events, e => e.Action == "core.identity.break-glass.requested" && e.ResourceId == grant.Id.ToString());
        Assert.Contains(events, e => e.Action == "core.identity.break-glass.approved" && e.ActorUserId == op2);
        Assert.Contains(events, e => e.Action == "core.security.permission.denied" && e.ActorUserId == op1);
        Assert.True((await (await _api.As(admin, tenant).GetAsync("/api/core/audit/verify", Ct)).Read<AuditVerificationDto>()).Intact);
    }

    [Fact]
    public async Task Expired_grants_give_no_access()
    {
        PostgresFixture.SkipIfUnavailable();
        var op1 = await NewOperatorAsync();
        var op2 = await NewOperatorAsync();
        var (tenant, _) = await NewTenantAsync(op1);

        await using (var sp = db.BuildServices())
        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
            var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            ctx.BreakGlassGrants.Add(new BreakGlassGrant
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, UserId = op1, Reason = "Expired synthetic grant",
                RequestedAt = now.AddHours(-3), ExpiresAt = now.AddMinutes(-1), ApprovedBy = op2, ApprovedAt = now.AddHours(-3),
            });
            await ctx.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op1, tenant).GetAsync("/api/core/tenancy/organizations", Ct)).StatusCode);
    }

    [Fact]
    public async Task Requests_need_a_reason_and_a_bounded_duration()
    {
        PostgresFixture.SkipIfUnavailable();
        var op = await NewOperatorAsync();
        var (tenant, _) = await NewTenantAsync(op);

        Assert.Equal(HttpStatusCode.BadRequest, (await Request(op, tenant, reason: "because")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(op, tenant, minutes: 600)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(op, mfa: false).PostAsJsonAsync($"/api/platform/tenants/{tenant}/break-glass",
            new BreakGlassRequest("Support ticket HOS-1234: login issue", 30), Ct)).StatusCode);
    }
}
