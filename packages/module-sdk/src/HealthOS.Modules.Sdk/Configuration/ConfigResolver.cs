using System.Globalization;
using System.Text.Json;

namespace HealthOS.Modules.Sdk.Configuration;

/// <summary>
/// Configuration precedence (docs/architecture/configuration-and-entitlements.md):
/// default → country → industry → specialty → organization → site → role → user.
/// Each level overrides the previous unless a less specific level locked the value; constraints
/// accumulate (each level may only narrow the range); several roles are merged by the setting's
/// strategy; only entries effective "now" participate. Pure and deterministic.
/// </summary>
public static class ConfigResolver
{
    public static ResolvedSetting Resolve(SettingDefinition definition, IEnumerable<SettingEntry> entries, ConfigContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(context);

        var trace = new List<ResolutionStep> { new(ConfigScope.Global, "default", "default", Show(definition.Default)) };
        var problems = new List<string>();
        var value = definition.Default;
        var source = ConfigScope.Global;
        var sourceKey = "default";
        ConfigScope? lockedAt = null;
        var min = definition.Min;
        var max = definition.Max;

        var applicable = entries
            .Where(e => e.Key == definition.Key && context.Matches(e) && IsEffective(e, context.Now))
            .ToList();

        foreach (var scope in Enum.GetValues<ConfigScope>())
        {
            var atLevel = applicable.Where(e => e.Scope == scope).ToList();
            if (atLevel.Count == 0)
            {
                continue;
            }

            if (!definition.AllowedScopes.Contains(scope))
            {
                trace.AddRange(atLevel.Select(e => new ResolutionStep(scope, e.ScopeKey, "ignored: scope not allowed for this setting", Show(e.Value))));
                continue;
            }

            // Constraints only ever narrow, even from a level whose value is ignored because of a lock.
            foreach (var e in atLevel)
            {
                if (e.Min is { } eMin && (min is null || eMin > min))
                {
                    min = eMin;
                    trace.Add(new(scope, e.ScopeKey, $"narrowed minimum to {eMin.ToString(CultureInfo.InvariantCulture)}", null));
                }

                if (e.Max is { } eMax && (max is null || eMax < max))
                {
                    max = eMax;
                    trace.Add(new(scope, e.ScopeKey, $"narrowed maximum to {eMax.ToString(CultureInfo.InvariantCulture)}", null));
                }
            }

            var withValue = atLevel.Where(e => e.Value is not null).ToList();
            if (withValue.Count > 0)
            {
                if (lockedAt is { } l)
                {
                    trace.AddRange(withValue.Select(e => new ResolutionStep(scope, e.ScopeKey, $"ignored: locked at {l}", Show(e.Value))));
                }
                else
                {
                    var chosen = scope == ConfigScope.Role ? MergeRoles(definition, withValue, context) : withValue[0];
                    value = chosen.Value!.Value;
                    source = scope;
                    sourceKey = chosen.ScopeKey;
                    trace.Add(new(scope, chosen.ScopeKey, withValue.Count > 1 ? $"applied ({definition.Merge} of {withValue.Count} roles)" : "applied", Show(value)));
                }
            }

            if (lockedAt is null && atLevel.Any(e => e.Locked))
            {
                lockedAt = scope;
                trace.Add(new(scope, atLevel.First(e => e.Locked).ScopeKey, "locked", null));
            }
        }

        problems.AddRange(ValidateValue(definition, value, min, max));
        return new ResolvedSetting(definition.Key, value, source, sourceKey, lockedAt, min, max, trace, problems);
    }

    /// <summary>
    /// Authoring-time validation of one entry: allowed scope, value type and range, and that no less
    /// specific entry (e.g. a regulatory profile) has locked the setting or narrowed the range.
    /// </summary>
    public static IReadOnlyList<string> ValidateEntry(SettingDefinition definition, SettingEntry entry, IEnumerable<SettingEntry> lessSpecific)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(lessSpecific);
        var problems = new List<string>();

        if (!definition.AllowedScopes.Contains(entry.Scope))
        {
            problems.Add($"cannot be set at {entry.Scope} level (allowed: {string.Join(", ", definition.AllowedScopes)})");
        }

        if (entry.Scope != ConfigScope.Global && string.IsNullOrWhiteSpace(entry.ScopeKey))
        {
            problems.Add($"{entry.Scope} entries need a scope key");
        }

        if (entry.EffectiveFrom is { } f && entry.EffectiveTo is { } t && t <= f)
        {
            problems.Add("effectiveTo must be after effectiveFrom");
        }

        var above = lessSpecific.Where(e => e.Key == definition.Key && e.Scope < entry.Scope).ToList();
        if (above.FirstOrDefault(e => e.Locked) is { } lockedBy && entry.Value is not null)
        {
            problems.Add($"locked at {lockedBy.Scope} level ({lockedBy.ScopeKey})");
        }

        var min = above.Where(e => e.Min is not null).Select(e => e.Min).Append(definition.Min).Max();
        var max = above.Where(e => e.Max is not null).Select(e => e.Max).Append(definition.Max).Where(x => x is not null).DefaultIfEmpty(null).Min();
        if (entry.Value is { } v)
        {
            problems.AddRange(ValidateValue(definition, v, min, max));
        }

        return problems;
    }

    /// <summary>Checks a value against the definition's type and the given (possibly narrowed) range.</summary>
    public static IReadOnlyList<string> ValidateValue(SettingDefinition d, JsonElement value, decimal? min = null, decimal? max = null)
    {
        ArgumentNullException.ThrowIfNull(d);
        var problems = new List<string>();
        min ??= d.Min;
        max ??= d.Max;

        switch (d.Type)
        {
            case SettingType.Boolean when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False):
                problems.Add("must be true or false");
                break;
            case SettingType.Integer when value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _):
                problems.Add("must be a whole number");
                break;
            case SettingType.Decimal when value.ValueKind != JsonValueKind.Number:
                problems.Add("must be a number");
                break;
            case SettingType.String when value.ValueKind != JsonValueKind.String:
                problems.Add("must be text");
                break;
            case SettingType.String when d.MaxLength is { } ml && value.GetString()!.Length > ml:
                problems.Add($"must be at most {ml} characters");
                break;
            case SettingType.Enum when value.ValueKind != JsonValueKind.String || !d.Options.Contains(value.GetString()!, StringComparer.Ordinal):
                problems.Add($"must be one of: {string.Join(", ", d.Options)}");
                break;
        }

        if (problems.Count == 0 && d.Type is SettingType.Integer or SettingType.Decimal)
        {
            var n = value.GetDecimal();
            if (min is { } lo && n < lo)
            {
                problems.Add($"must be at least {lo.ToString(CultureInfo.InvariantCulture)}");
            }

            if (max is { } hi && n > hi)
            {
                problems.Add($"must be at most {hi.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return problems;
    }

    private static SettingEntry MergeRoles(SettingDefinition d, List<SettingEntry> entries, ConfigContext context)
    {
        if (entries.Count == 1)
        {
            return entries[0];
        }

        if (d.Merge == MergeStrategy.FirstRole || d.Type is SettingType.String or SettingType.Enum)
        {
            return entries.OrderBy(e => IndexOf(context.Roles, e.ScopeKey)).First();
        }

        var restrictive = d.Merge == MergeStrategy.MostRestrictive;
        return d.Type == SettingType.Boolean
            ? entries.OrderBy(e => e.Value!.Value.GetBoolean() == restrictive ? 1 : 0).First() // restrictive: false first
            : restrictive
                ? entries.OrderBy(e => e.Value!.Value.GetDecimal()).First()
                : entries.OrderByDescending(e => e.Value!.Value.GetDecimal()).First();
    }

    private static int IndexOf(IReadOnlyList<string> roles, string role)
    {
        for (var i = 0; i < roles.Count; i++)
        {
            if (string.Equals(roles[i], role, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static bool IsEffective(SettingEntry e, DateTimeOffset now) =>
        (e.EffectiveFrom is null || e.EffectiveFrom <= now) && (e.EffectiveTo is null || now < e.EffectiveTo);

    private static string? Show(JsonElement? v) => v?.GetRawText();
}
