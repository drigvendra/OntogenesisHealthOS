using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthOS.Core.Configuration;
using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy;
using HealthOS.Core.Tenancy.Contracts;
using HealthOS.Modules.Sdk.Configuration;
using HealthOS.Modules.Sdk.Context;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>M3 gate evidence: configuration inheritance, validation, preview, approval, publish, rollback, isolation.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class ConfigurationApiTests(PostgresFixture db) : IAsyncLifetime
{
    private const string IdleTimeout = "core.identity.session.idleTimeoutMinutes";
    private const string FourEyes = "core.configuration.approval.fourEyes";
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

    private async Task<(Guid Operator, Guid Tenant, Guid Admin1, Guid Admin2)> SetupAsync(string industry)
    {
        var (op, opEmail) = await NewUserAsync("operator");
        await using (var sp = db.BuildServices())
        await using (var scope = sp.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
            await scope.ServiceProvider.GetRequiredService<IOperatorAdministration>().GrantOperatorByEmailAsync(opEmail, Ct);
        }

        var t = await (await _api.As(op).PostAsJsonAsync("/api/platform/tenants",
            new CreateTenantRequest("c" + Guid.NewGuid().ToString("N")[..10], "Synthetic config"), Ct)).Read<TenantSummary>();
        Assert.Equal(HttpStatusCode.OK, (await _api.As(op).PutAsJsonAsync($"/api/platform/tenants/{t.Id}/configuration/profile",
            new SetProfileRequest("IN", industry, null), Ct)).StatusCode);

        var admins = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var (admin, email) = await NewUserAsync("admin");
            Assert.Equal(HttpStatusCode.NoContent, (await _api.As(op).PostAsJsonAsync($"/api/platform/tenants/{t.Id}/admins", new { email }, Ct)).StatusCode);
            admins.Add(admin);
        }

        return (op, t.Id, admins[0], admins[1]);
    }

    private static SettingEntry Entry(string key, ConfigScope scope, string scopeKey, object value) =>
        new() { Key = key, Scope = scope, ScopeKey = scopeKey, Value = JsonSerializer.SerializeToElement(value) };

    private async Task<HttpResponseMessage> Draft(Guid user, Guid tenant, params SettingEntry[] entries) =>
        await _api.As(user, tenant).PostAsJsonAsync("/api/core/configuration/drafts", new CreateDraftRequest("Synthetic change", entries), ConfigurationService.Json, Ct);

    private async Task<List<ResolvedSetting>> Effective(Guid user, Guid tenant) =>
        (await (await _api.As(user, tenant).GetAsync("/api/core/configuration/effective", Ct)).Content.ReadFromJsonAsync<List<ResolvedSetting>>(ConfigurationService.Json, Ct))!;

    [Fact]
    public async Task Draft_preview_approve_publish_changes_effective_values_with_provenance()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin, _) = await SetupAsync("clinic");

        // Before: the clinic industry profile supplies 30 minutes.
        var before = Assert.Single(await Effective(admin, t), s => s.Key == IdleTimeout);
        Assert.Equal(30, before.Value.GetInt32());
        Assert.Equal(ConfigScope.Industry, before.Source);

        var draft = await (await Draft(admin, t, Entry(IdleTimeout, ConfigScope.Role, "tenant-admin", 20))).Content.ReadFromJsonAsync<DraftDto>(ConfigurationService.Json, Ct);
        var preview = await (await _api.As(admin, t).PostAsJsonAsync($"/api/core/configuration/drafts/{draft!.Id}/preview",
            new PreviewRequest(null, null, ["tenant-admin"], null), Ct)).Content.ReadFromJsonAsync<List<ResolvedSetting>>(ConfigurationService.Json, Ct);
        Assert.Equal(20, Assert.Single(preview!, s => s.Key == IdleTimeout).Value.GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).PostAsync($"/api/core/configuration/drafts/{draft.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).PostAsync($"/api/core/configuration/drafts/{draft.Id}/publish", null, Ct)).StatusCode);

        var after = Assert.Single(await Effective(admin, t), s => s.Key == IdleTimeout);
        Assert.Equal(20, after.Value.GetInt32());
        Assert.Equal(ConfigScope.Role, after.Source);
        Assert.Contains(after.Trace, step => step.Scope == ConfigScope.Industry);
    }

    [Fact]
    public async Task Tenants_cannot_set_platform_levels_or_override_locks_or_exceed_ranges()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin, _) = await SetupAsync("pharma-qa");

        Assert.Equal(HttpStatusCode.BadRequest, (await Draft(admin, t, Entry(IdleTimeout, ConfigScope.Country, "IN", 10))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Draft(admin, t, Entry(FourEyes, ConfigScope.Organization, "org-1", false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Draft(admin, t, Entry("core.audit.retention.years", ConfigScope.Organization, "org-1", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Draft(admin, t, Entry("no.such.setting", ConfigScope.Organization, "org-1", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Draft(admin, t, Entry("core.audit.retention.years", ConfigScope.Organization, "org-1", 12))).StatusCode);
    }

    [Fact]
    public async Task Four_eyes_rule_from_the_pharma_profile_blocks_self_approval()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, author, reviewer) = await SetupAsync("pharma-qa");

        var draft = await (await Draft(author, t, Entry(IdleTimeout, ConfigScope.Role, "tenant-admin", 15))).Content.ReadFromJsonAsync<DraftDto>(ConfigurationService.Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.As(author, t).PostAsync($"/api/core/configuration/drafts/{draft!.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.As(reviewer, t).PostAsync($"/api/core/configuration/drafts/{draft.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _api.As(author, t).PostAsync($"/api/core/configuration/drafts/{draft.Id}/approve", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Stale_drafts_cannot_be_published_and_rollback_restores_previous_values()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin, _) = await SetupAsync("clinic");

        var d1 = await (await Draft(admin, t, Entry(IdleTimeout, ConfigScope.Role, "tenant-admin", 20))).Content.ReadFromJsonAsync<DraftDto>(ConfigurationService.Json, Ct);
        var d2 = await (await Draft(admin, t, Entry(IdleTimeout, ConfigScope.Role, "tenant-admin", 10))).Content.ReadFromJsonAsync<DraftDto>(ConfigurationService.Json, Ct);
        foreach (var d in new[] { d1!, d2! })
        {
            await _api.As(admin, t).PostAsync($"/api/core/configuration/drafts/{d.Id}/approve", null, Ct);
        }

        Assert.Equal(HttpStatusCode.OK, (await _api.As(admin, t).PostAsync($"/api/core/configuration/drafts/{d1!.Id}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _api.As(admin, t).PostAsync($"/api/core/configuration/drafts/{d2!.Id}/publish", null, Ct)).StatusCode);
        Assert.Equal(20, Assert.Single(await Effective(admin, t), s => s.Key == IdleTimeout).Value.GetInt32());

        var rolled = await (await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/rollback", new RollbackRequest(0), Ct)).Read<SnapshotDto>();
        Assert.Equal(2, rolled.Version);
        Assert.Equal(0, rolled.RolledBackTo);
        Assert.Equal(30, Assert.Single(await Effective(admin, t), s => s.Key == IdleTimeout).Value.GetInt32());

        var history = await (await _api.As(admin, t).GetAsync("/api/core/configuration/snapshots", Ct)).Read<List<SnapshotDto>>();
        Assert.Equal([2, 1], history.Select(h => h.Version));
    }

    private static object IntakeForm(string extraLabel = "Allergies") => new
    {
        code = "general-intake",
        title = "General intake",
        fields = new object[]
        {
            new { id = "painLevel", label = "Pain (0-10)", type = "Integer", required = true, min = 0, max = 10 },
            new { id = "allergies", label = extraLabel, type = "TextArea", maxLength = 500, classification = "Phi",
                  visibleWhen = new { field = "painLevel", op = "isSet" } },
        },
    };

    [Fact]
    public async Task Forms_are_validated_versioned_and_check_submitted_values()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin, _) = await SetupAsync("clinic");

        var invalid = await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/forms",
            new { code = "Bad", title = "", fields = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/forms", IntakeForm(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/forms", IntakeForm("Known allergies"), Ct)).StatusCode);

        var list = await (await _api.As(admin, t).GetAsync("/api/core/configuration/forms", Ct)).Read<List<DefinitionSummary>>();
        Assert.Equal(2, Assert.Single(list, f => f.Code == "general-intake").Version);
        var v1 = await (await _api.As(admin, t).GetAsync("/api/core/configuration/forms/general-intake?version=1", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"Allergies\"", v1, StringComparison.Ordinal);

        var check = await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/forms/general-intake/validate-values",
            new { values = new Dictionary<string, object> { ["painLevel"] = 12 } }, Ct);
        Assert.Contains("at most 10", await check.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workflows_must_be_sound_and_use_known_permissions()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, t, admin, _) = await SetupAsync("clinic");

        object Workflow(string permission) => new
        {
            code = "member-approval",
            title = "Member approval",
            initialState = "requested",
            states = new object[] { new { id = "requested", label = "Requested", slaHours = 48 }, new { id = "approved", label = "Approved", terminal = true } },
            transitions = new object[] { new { id = "approve", from = "requested", to = "approved", label = "Approve", permission, requiresDifferentActor = true } },
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/workflows", Workflow("made.up.permission"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _api.As(admin, t).PostAsJsonAsync("/api/core/configuration/workflows", Workflow("core.identity.users.manage"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Published_definitions_cannot_be_modified()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(ConfigurationDbContext.SchemaName)}\"");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, "DELETE FROM core_configuration.definition"));
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task Snapshots_are_immutable_and_drafts_are_tenant_private()
    {
        PostgresFixture.SkipIfUnavailable();
        var (_, a, adminA, _) = await SetupAsync("clinic");
        var (_, b, adminB, _) = await SetupAsync("clinic");

        var draft = await (await Draft(adminA, a, Entry(IdleTimeout, ConfigScope.Role, "tenant-admin", 20))).Content.ReadFromJsonAsync<DraftDto>(ConfigurationService.Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.As(adminB, b).GetAsync($"/api/core/configuration/drafts/{draft!.Id}", Ct)).StatusCode);

        await using var c = new NpgsqlConnection(db.AppConnectionString);
        await c.OpenAsync(Ct);
        await PostgresFixture.Exec(c, $"SET ROLE \"{db.Roles.ForModuleSchema(ConfigurationDbContext.SchemaName)}\"");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.Exec(c, "UPDATE core_configuration.config_snapshot SET entries = '[]'"));
        Assert.Equal("42501", ex.SqlState);
    }
}
