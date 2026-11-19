using System.Net;
using System.Net.Http.Json;
using HealthOS.Core.Audit;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>M2 gate evidence: audit events are written, isolated per tenant, append-only and tamper-evident.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AuditTrailTests(PostgresFixture db) : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";
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

    [Fact]
    public async Task Tenant_actions_are_recorded_in_the_tenants_own_chain()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, a, adminA) = await SetupTenantAsync("ea");
        var (_, b, adminB) = await SetupTenantAsync("eb");

        var org = await (await _api.As(adminA, a).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Audited org"), Ct)).Read<OrganizationDto>();

        var eventsA = await (await _api.As(adminA, a).GetAsync("/api/core/audit/events", Ct)).Read<List<AuditEventDto>>();
        var created = Assert.Single(eventsA, e => e.Action == "core.tenancy.organization.created");
        Assert.Equal(org.Id.ToString(), created.ResourceId);
        Assert.Equal(adminA, created.ActorUserId);
        // Operator actions (e.g. appointing this admin) live in the platform chain, not the tenant's.
        Assert.DoesNotContain(eventsA, e => e.Action == "core.identity.tenant-admin.appointed");

        var eventsB = await (await _api.As(adminB, b).GetAsync("/api/core/audit/events", Ct)).Read<List<AuditEventDto>>();
        Assert.DoesNotContain(eventsB, e => e.ResourceId == org.Id.ToString());

        var verify = await (await _api.As(adminA, a).GetAsync("/api/core/audit/verify", Ct)).Read<AuditVerificationDto>();
        Assert.True(verify.Intact);
    }

    [Fact]
    public async Task Platform_actions_are_recorded_in_the_platform_chain()
    {
        PostgresFixture.SkipIfUnavailable();
        var (op, tenant, _) = await SetupTenantAsync("ep");

        var events = await (await _api.As(op).GetAsync("/api/platform/audit/events?limit=500", Ct)).Read<List<AuditEventDto>>();
        Assert.Contains(events, e => e.Action == "core.tenancy.tenant.created" && e.ResourceId == tenant.ToString() && e.ActorUserId == op);
        Assert.Contains(events, e => e.Action == "core.identity.tenant-admin.appointed" && e.ResourceId == tenant.ToString());
        Assert.True((await (await _api.As(op).GetAsync("/api/platform/audit/verify", Ct)).Read<AuditVerificationDto>()).Intact);
    }

    [Fact]
    public async Task Permission_denials_are_audited()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin) = await SetupTenantAsync("ed");
        var (member, email) = await NewUserAsync("member");
        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t).PostAsJsonAsync("/api/core/identity/members", new { email }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(member, t).GetAsync("/api/core/tenancy/sites", Ct)).StatusCode);

        var events = await (await _api.As(admin, t).GetAsync("/api/core/audit/events", Ct)).Read<List<AuditEventDto>>();
        var denied = Assert.Single(events, e => e.Action == "core.security.permission.denied" && e.ActorUserId == member);
        Assert.Equal("denied", denied.Outcome);
        Assert.Equal("core.tenancy.structure.read", denied.ResourceId);
    }

    [Fact]
    public async Task Audit_rows_cannot_be_written_directly_by_any_module_role()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);

        foreach (var schema in new[] { "core_audit", "core_identity", "core_tenancy" })
        {
            await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(schema)}\"");
            var update = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, "UPDATE core_audit.audit_event SET action = 'x'"));
            Assert.Equal(InsufficientPrivilege, update.SqlState);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, "DELETE FROM core_audit.audit_event"));
            Assert.Equal(InsufficientPrivilege, delete.SqlState);
            await PostgresFixture.Exec(c, "RESET ROLE");
        }
    }

    [Fact]
    public async Task Append_without_tenant_or_platform_context_is_refused()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema("core_identity")}\"");
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresFixture.Exec(c, "SELECT core_audit.append('x.y.z', 'thing', NULL, 'success', '{}'::jsonb)"));
        Assert.Equal(InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Tampering_is_detected_by_verification()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin) = await SetupTenantAsync("et");
        await _api.As(admin, t).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("One"), Ct);
        await _api.As(admin, t).PostAsJsonAsync("/api/core/tenancy/organizations", new CreateOrganizationRequest("Two"), Ct);
        Assert.True((await (await _api.As(admin, t).GetAsync("/api/core/audit/verify", Ct)).Read<AuditVerificationDto>()).Intact);

        // A database superuser rewrites history: verification must notice.
        long tamperedSeq;
        await using (var c = new NpgsqlConnection(db.AdminDatabaseConnectionString))
        {
            await c.OpenAsync(Ct);
            tamperedSeq = (long)(await PostgresFixture.Scalar(c,
                $"UPDATE core_audit.audit_event SET resource_type = 'forged' WHERE tenant_id = '{t}' AND seq = 2 RETURNING seq"))!;
        }

        var after = await (await _api.As(admin, t).GetAsync("/api/core/audit/verify", Ct)).Read<AuditVerificationDto>();
        Assert.False(after.Intact);
        Assert.Equal(tamperedSeq, after.FirstInvalidSeq);
    }
}
