using System.Text.Json;
using HealthOS.Modules.Sdk.Configuration;

namespace HealthOS.Modules.Sdk.Tests;

public class ConfigResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    private static readonly SettingDefinition ReviewMonths = new()
    {
        Key = "careops.document.reviewIntervalMonths",
        Type = SettingType.Integer,
        Description = "Review interval",
        Default = J(24),
        Min = 1,
        Max = 60,
        AllowedScopes = Enum.GetValues<ConfigScope>(),
        Merge = MergeStrategy.MostRestrictive,
    };

    private static readonly ConfigContext Ctx = new()
    {
        Country = "IN",
        Industry = "pharma-qa",
        Specialty = "none",
        OrganizationId = "org-1",
        SiteId = "site-1",
        Roles = ["qa-lead", "author"],
        UserId = "user-1",
        Now = Now,
    };

    private static SettingEntry E(ConfigScope scope, string key, object? value, bool locked = false, decimal? min = null, decimal? max = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null) => new()
    {
        Key = ReviewMonths.Key, Scope = scope, ScopeKey = key, Value = value is null ? null : J(value), Locked = locked, Min = min, Max = max,
        EffectiveFrom = from, EffectiveTo = to,
    };

    [Fact]
    public void Default_applies_when_nothing_is_set()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [], Ctx);
        Assert.Equal(24, r.Value.GetInt32());
        Assert.Equal(ConfigScope.Global, r.Source);
    }

    [Fact]
    public void Each_more_specific_level_overrides_the_previous_one()
    {
        var entries = new List<SettingEntry>();
        var expected = 10;
        foreach (var (scope, key) in new[] { (ConfigScope.Country, "IN"), (ConfigScope.Industry, "pharma-qa"), (ConfigScope.Specialty, "none"),
                     (ConfigScope.Organization, "org-1"), (ConfigScope.Site, "site-1"), (ConfigScope.Role, "qa-lead"), (ConfigScope.User, "user-1") })
        {
            entries.Add(E(scope, key, expected));
            var r = ConfigResolver.Resolve(ReviewMonths, entries, Ctx);
            Assert.Equal(expected, r.Value.GetInt32());
            Assert.Equal(scope, r.Source);
            expected++;
        }
    }

    [Fact]
    public void Entries_for_other_contexts_do_not_apply()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [E(ConfigScope.Country, "DE", 6), E(ConfigScope.Site, "site-2", 3), E(ConfigScope.User, "user-2", 2)], Ctx);
        Assert.Equal(24, r.Value.GetInt32());
    }

    [Fact]
    public void A_lock_stops_more_specific_overrides()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [E(ConfigScope.Industry, "pharma-qa", 12, locked: true), E(ConfigScope.Organization, "org-1", 36), E(ConfigScope.User, "user-1", 48)], Ctx);
        Assert.Equal(12, r.Value.GetInt32());
        Assert.Equal(ConfigScope.Industry, r.LockedAt);
        Assert.Contains(r.Trace, t => t.Scope == ConfigScope.Organization && t.Effect.StartsWith("ignored: locked", StringComparison.Ordinal));
    }

    [Fact]
    public void Constraints_only_narrow_and_out_of_range_values_are_reported()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [E(ConfigScope.Country, "IN", null, max: 24), E(ConfigScope.Organization, "org-1", null, max: 48), E(ConfigScope.Site, "site-1", 36)], Ctx);
        Assert.Equal(24m, r.Max); // org tried to widen to 48: ignored
        Assert.Contains(r.Problems, p => p.Contains("at most 24", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_roles_are_merged_by_strategy()
    {
        var entries = new[] { E(ConfigScope.Role, "qa-lead", 18), E(ConfigScope.Role, "author", 6) };
        Assert.Equal(6, ConfigResolver.Resolve(ReviewMonths, entries, Ctx).Value.GetInt32());
        Assert.Equal(18, ConfigResolver.Resolve(ReviewMonths with { Merge = MergeStrategy.MostPermissive }, entries, Ctx).Value.GetInt32());
        Assert.Equal(18, ConfigResolver.Resolve(ReviewMonths with { Merge = MergeStrategy.FirstRole }, entries, Ctx).Value.GetInt32());
    }

    [Fact]
    public void Only_currently_effective_entries_apply()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [E(ConfigScope.Organization, "org-1", 6, to: Now.AddDays(-1)), E(ConfigScope.Site, "site-1", 9, from: Now.AddDays(1))], Ctx);
        Assert.Equal(24, r.Value.GetInt32());
    }

    [Fact]
    public void Entries_at_disallowed_scopes_are_ignored_and_rejected_when_authored()
    {
        var limited = ReviewMonths with { AllowedScopes = [ConfigScope.Global, ConfigScope.Organization] };
        Assert.Equal(24, ConfigResolver.Resolve(limited, [E(ConfigScope.User, "user-1", 3)], Ctx).Value.GetInt32());
        Assert.NotEmpty(ConfigResolver.ValidateEntry(limited, E(ConfigScope.User, "user-1", 3), []));
    }

    [Fact]
    public void Authoring_rejects_values_under_a_lock_or_outside_narrowed_range()
    {
        Assert.Contains(ConfigResolver.ValidateEntry(ReviewMonths, E(ConfigScope.Organization, "org-1", 36), [E(ConfigScope.Industry, "pharma-qa", 12, locked: true)]),
            p => p.Contains("locked", StringComparison.Ordinal));
        Assert.Contains(ConfigResolver.ValidateEntry(ReviewMonths, E(ConfigScope.Organization, "org-1", 36), [E(ConfigScope.Country, "IN", null, max: 24)]),
            p => p.Contains("at most 24", StringComparison.Ordinal));
        Assert.Empty(ConfigResolver.ValidateEntry(ReviewMonths, E(ConfigScope.Organization, "org-1", 18), [E(ConfigScope.Country, "IN", null, max: 24)]));
    }

    [Theory]
    [InlineData(SettingType.Boolean, "\"yes\"")]
    [InlineData(SettingType.Integer, "1.5")]
    [InlineData(SettingType.String, "5")]
    [InlineData(SettingType.Enum, "\"purple\"")]
    public void Type_checks(SettingType type, string json)
    {
        var d = new SettingDefinition { Key = "x.y.z", Type = type, Description = "", Default = J(true), Options = ["red", "green"] };
        Assert.NotEmpty(ConfigResolver.ValidateValue(d, JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void Trace_explains_the_result()
    {
        var r = ConfigResolver.Resolve(ReviewMonths, [E(ConfigScope.Country, "IN", 12), E(ConfigScope.Site, "site-1", 6)], Ctx);
        Assert.Equal(["default", "applied", "applied"], r.Trace.Select(t => t.Effect));
        Assert.Equal("site-1", r.SourceKey);
    }
}
