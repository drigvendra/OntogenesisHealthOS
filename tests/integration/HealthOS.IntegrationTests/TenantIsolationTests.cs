using HealthOS.Core.Identity.Contracts;
using HealthOS.Core.Identity.Data;
using HealthOS.Core.Tenancy.Data;
using HealthOS.Modules.Sdk.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HealthOS.IntegrationTests;

/// <summary>
/// M2 gate evidence at the database layer: tenant A cannot read or write tenant B's rows, enforced
/// by PostgreSQL row-level security on the real module connections (not by application filters).
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class TenantIsolationTests(PostgresFixture db)
{
    private const string RlsViolation = "42501";
    private const string ForeignKeyViolation = "23503";

    private sealed record Seed(Guid TenantA, Guid TenantB, Guid OrgA, Guid OrgB);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<T> InScope<T>(ServiceProvider sp, Action<RequestContext> setup, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = sp.CreateAsyncScope();
        setup(scope.ServiceProvider.GetRequiredService<RequestContext>());
        return await work(scope.ServiceProvider);
    }

    private async Task<Seed> SeedTwoTenantsAsync(ServiceProvider sp)
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        await InScope(sp, c => c.EnterPlatformScope(), async s =>
        {
            var t = s.GetRequiredService<TenancyDbContext>();
            t.Tenants.Add(new Tenant { Id = a, Code = "a" + a.ToString("N")[..12], Name = "Synthetic A", RegionCell = "TEST-1", CreatedAt = DateTimeOffset.UtcNow });
            t.Tenants.Add(new Tenant { Id = b, Code = "b" + b.ToString("N")[..12], Name = "Synthetic B", RegionCell = "TEST-1", CreatedAt = DateTimeOffset.UtcNow });
            return await t.SaveChangesAsync(Ct);
        });

        async Task<Guid> AddOrg(Guid tenant, string name) => await InScope(sp, c => c.SetTenant(tenant), async s =>
        {
            var t = s.GetRequiredService<TenancyDbContext>();
            var org = new Organization { Id = Guid.CreateVersion7(), TenantId = tenant, Name = name, CreatedAt = DateTimeOffset.UtcNow };
            t.Organizations.Add(org);
            await t.SaveChangesAsync(Ct);
            return org.Id;
        });

        return new Seed(a, b, await AddOrg(a, "Org of A"), await AddOrg(b, "Org of B"));
    }

    [Fact]
    public async Task Every_table_with_tenant_id_has_forced_row_level_security()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var c = new NpgsqlConnection(db.AdminDatabaseConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT n.nspname || '.' || cl.relname
              FROM information_schema.columns col
              JOIN pg_namespace n ON n.nspname = col.table_schema
              JOIN pg_class cl ON cl.relnamespace = n.oid AND cl.relname = col.table_name AND cl.relkind = 'r'
             WHERE col.column_name = 'tenant_id'
               AND (NOT cl.relrowsecurity OR NOT cl.relforcerowsecurity
                    OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = cl.oid))
            """, c);
        var offenders = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync(Ct))
        {
            while (await r.ReadAsync(Ct))
            {
                offenders.Add(r.GetString(0));
            }
        }

        Assert.True(offenders.Count == 0, "Tables with tenant_id lacking forced RLS: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task Tenant_sees_only_its_own_rows()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var visible = await InScope(sp, c => c.SetTenant(seed.TenantA), s =>
            s.GetRequiredService<TenancyDbContext>().Organizations.Select(o => o.Id).ToListAsync(Ct));

        Assert.Contains(seed.OrgA, visible);
        Assert.DoesNotContain(seed.OrgB, visible);
        Assert.All(visible, id => Assert.NotEqual(seed.OrgB, id));
    }

    [Fact]
    public async Task Tenant_cannot_insert_rows_for_another_tenant()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(sp, c => c.SetTenant(seed.TenantA), async s =>
        {
            var t = s.GetRequiredService<TenancyDbContext>();
            t.Organizations.Add(new Organization { Id = Guid.CreateVersion7(), TenantId = seed.TenantB, Name = "Smuggled", CreatedAt = DateTimeOffset.UtcNow });
            return await t.SaveChangesAsync(Ct);
        }));
        Assert.Equal(RlsViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Tenant_cannot_update_or_delete_another_tenants_rows()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var (updated, deleted) = await InScope(sp, c => c.SetTenant(seed.TenantA), async s =>
        {
            var t = s.GetRequiredService<TenancyDbContext>();
            var u = await t.Organizations.Where(o => o.Id == seed.OrgB).ExecuteUpdateAsync(x => x.SetProperty(o => o.Name, "Hijacked"), Ct);
            var d = await t.Organizations.Where(o => o.Id == seed.OrgB).ExecuteDeleteAsync(Ct);
            return (u, d);
        });

        Assert.Equal(0, updated);
        Assert.Equal(0, deleted);
        var name = await InScope(sp, c => c.SetTenant(seed.TenantB), s =>
            s.GetRequiredService<TenancyDbContext>().Organizations.Where(o => o.Id == seed.OrgB).Select(o => o.Name).SingleAsync(Ct));
        Assert.Equal("Org of B", name);
    }

    [Fact]
    public async Task Missing_context_sees_nothing_and_cannot_write()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var count = await InScope(sp, _ => { }, s => s.GetRequiredService<TenancyDbContext>().Organizations.CountAsync(Ct));
        Assert.Equal(0, count);

        await Assert.ThrowsAsync<DbUpdateException>(() => InScope(sp, _ => { }, async s =>
        {
            var t = s.GetRequiredService<TenancyDbContext>();
            t.Organizations.Add(new Organization { Id = Guid.CreateVersion7(), TenantId = seed.TenantA, Name = "No context", CreatedAt = DateTimeOffset.UtcNow });
            return await t.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task Platform_scope_cannot_see_tenant_internal_structure()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var count = await InScope(sp, c => c.EnterPlatformScope(), s =>
            s.GetRequiredService<TenancyDbContext>().Organizations.CountAsync(o => o.Id == seed.OrgA || o.Id == seed.OrgB, Ct));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Users_see_their_own_memberships_but_not_other_tenants_members()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);

        var (u, v) = await InScope(sp, _ => { }, async s =>
        {
            var dir = s.GetRequiredService<IUserDirectory>();
            return (await dir.EnsureUserAsync(new ExternalIdentity("https://idp.test", "u-" + Guid.NewGuid(), "U", null), Ct),
                    await dir.EnsureUserAsync(new ExternalIdentity("https://idp.test", "v-" + Guid.NewGuid(), "V", null), Ct));
        });

        await InScope(sp, c => c.EnterPlatformScope(), async s =>
        {
            var i = s.GetRequiredService<IdentityDbContext>();
            i.Memberships.Add(new TenantMembership { TenantId = seed.TenantA, UserId = u, CreatedAt = DateTimeOffset.UtcNow });
            i.Memberships.Add(new TenantMembership { TenantId = seed.TenantB, UserId = u, CreatedAt = DateTimeOffset.UtcNow });
            i.Memberships.Add(new TenantMembership { TenantId = seed.TenantB, UserId = v, CreatedAt = DateTimeOffset.UtcNow });
            return await i.SaveChangesAsync(Ct);
        });

        // U, without a tenant selected, can list both of their own tenants (to choose one).
        var uTenants = await InScope(sp, c => c.SetUser(u), s => s.GetRequiredService<IAccessControl>().GetActiveTenantIdsAsync(u, Ct));
        Assert.Equal(new[] { seed.TenantA, seed.TenantB }.Order(), uTenants.Order());

        // U cannot see V's membership in tenant B while not in tenant B.
        var seenByU = await InScope(sp, c => c.SetUser(u), s =>
            s.GetRequiredService<IdentityDbContext>().Memberships.CountAsync(m => m.UserId == v, Ct));
        Assert.Equal(0, seenByU);

        // In tenant A context, V's membership in B is invisible.
        var seenInA = await InScope(sp, c => { c.SetUser(u); c.SetTenant(seed.TenantA); }, s =>
            s.GetRequiredService<IdentityDbContext>().Memberships.CountAsync(m => m.UserId == v, Ct));
        Assert.Equal(0, seenInA);
    }

    [Fact]
    public async Task Role_assignment_cannot_reference_another_tenants_role()
    {
        PostgresFixture.SkipIfUnavailable();
        await using var sp = db.BuildServices();
        var seed = await SeedTwoTenantsAsync(sp);
        var user = await InScope(sp, _ => { }, s => s.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(new ExternalIdentity("https://idp.test", "w-" + Guid.NewGuid(), "W", null), Ct));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(sp, c => c.EnterPlatformScope(), async s =>
        {
            var i = s.GetRequiredService<IdentityDbContext>();
            var roleInB = new Role { Id = Guid.CreateVersion7(), TenantId = seed.TenantB, Code = "clerk", Name = "Clerk", CreatedAt = DateTimeOffset.UtcNow };
            i.Roles.Add(roleInB);
            i.Memberships.Add(new TenantMembership { TenantId = seed.TenantA, UserId = user, CreatedAt = DateTimeOffset.UtcNow });
            await i.SaveChangesAsync(Ct);

            // Assignment in tenant A pointing at tenant B's role.
            i.RoleAssignments.Add(new RoleAssignment { Id = Guid.CreateVersion7(), TenantId = seed.TenantA, UserId = user, RoleId = roleInB.Id, CreatedAt = DateTimeOffset.UtcNow });
            return await i.SaveChangesAsync(Ct);
        }));
        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }
}
