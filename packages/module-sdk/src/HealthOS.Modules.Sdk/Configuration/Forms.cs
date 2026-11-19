using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HealthOS.Modules.Sdk.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<FieldType>))]
public enum FieldType
{
    Text,
    TextArea,
    Integer,
    Decimal,
    Date,
    Boolean,
    Choice,
    MultiChoice,
}

[JsonConverter(typeof(JsonStringEnumConverter<DataClass>))]
public enum DataClass
{
    None,
    Pii,
    Phi,
}

/// <summary>
/// Declarative condition (data, not code): a comparison on another field's value, or all/any/not of conditions.
/// Operators: equals, notEquals, in, notIn, isSet, isNotSet, greaterThan, lessThan.
/// </summary>
public sealed record Condition
{
    public string? Field { get; init; }
    public string? Op { get; init; }
    public JsonElement? Value { get; init; }
    public IReadOnlyList<Condition>? All { get; init; }
    public IReadOnlyList<Condition>? Any { get; init; }
    public Condition? Not { get; init; }
}

public sealed record FieldDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required FieldType Type { get; init; }
    public bool Required { get; init; }
    public string? Unit { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public int? MaxLength { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public Condition? VisibleWhen { get; init; }
    public Condition? RequiredWhen { get; init; }

    /// <summary>Permission needed to view this field (e.g. for sensitive clinical details).</summary>
    public string? ViewPermission { get; init; }

    /// <summary>Terminology binding, e.g. "http://snomed.info/sct" (validation of codes is a later concern).</summary>
    public string? Terminology { get; init; }
    public DataClass Classification { get; init; } = DataClass.None;
}

public sealed record FormDefinition
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
}

/// <summary>Validation of form definitions and evaluation of submitted values. Pure and deterministic.</summary>
public static partial class FormEngine
{
    private static readonly string[] Ops = ["equals", "notEquals", "in", "notIn", "isSet", "isNotSet", "greaterThan", "lessThan"];

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-zA-Z0-9]{0,63}$")]
    private static partial Regex FieldIdPattern();

    public static IReadOnlyList<string> ValidateDefinition(FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var problems = new List<string>();
        if (!CodePattern().IsMatch(form.Code))
        {
            problems.Add("code: use 2-64 lowercase letters, digits or hyphens");
        }

        if (string.IsNullOrWhiteSpace(form.Title))
        {
            problems.Add("title is required");
        }

        if (form.Fields.Count == 0)
        {
            problems.Add("a form needs at least one field");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in form.Fields)
        {
            var p = $"field '{f.Id}'";
            if (!FieldIdPattern().IsMatch(f.Id))
            {
                problems.Add($"{p}: id must be camelCase letters/digits");
            }

            if (!ids.Add(f.Id))
            {
                problems.Add($"{p}: duplicate id");
            }

            if (string.IsNullOrWhiteSpace(f.Label))
            {
                problems.Add($"{p}: label is required");
            }

            if (f.Type is FieldType.Choice or FieldType.MultiChoice && f.Options.Count == 0)
            {
                problems.Add($"{p}: choice fields need options");
            }

            if (f.Min is { } lo && f.Max is { } hi && lo > hi)
            {
                problems.Add($"{p}: min is greater than max");
            }

            if ((f.Min is not null || f.Max is not null) && f.Type is not (FieldType.Integer or FieldType.Decimal))
            {
                problems.Add($"{p}: min/max only apply to numeric fields");
            }
        }

        var byId = form.Fields.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var f in form.Fields)
        {
            foreach (var (name, cond) in new[] { ("visibleWhen", f.VisibleWhen), ("requiredWhen", f.RequiredWhen) })
            {
                if (cond is not null)
                {
                    ValidateCondition(cond, f.Id, byId, $"field '{f.Id}' {name}", problems, depth: 0);
                }
            }
        }

        return problems;
    }

    private static void ValidateCondition(Condition c, string owner, Dictionary<string, FieldDefinition> fields, string where, List<string> problems, int depth)
    {
        if (depth > 8)
        {
            problems.Add($"{where}: conditions nested too deeply");
            return;
        }

        var shapes = (c.Field is not null ? 1 : 0) + (c.All is not null ? 1 : 0) + (c.Any is not null ? 1 : 0) + (c.Not is not null ? 1 : 0);
        if (shapes != 1)
        {
            problems.Add($"{where}: a condition is exactly one of field comparison, all, any or not");
            return;
        }

        if (c.Field is not null)
        {
            if (c.Field == owner)
            {
                problems.Add($"{where}: a field cannot depend on itself");
            }
            else if (!fields.ContainsKey(c.Field))
            {
                problems.Add($"{where}: unknown field '{c.Field}'");
            }

            if (c.Op is null || !Ops.Contains(c.Op))
            {
                problems.Add($"{where}: operator must be one of {string.Join(", ", Ops)}");
            }
            else if (c.Op is not ("isSet" or "isNotSet") && c.Value is null)
            {
                problems.Add($"{where}: operator '{c.Op}' needs a value");
            }
            else if (c.Op is "in" or "notIn" && c.Value?.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{where}: operator '{c.Op}' needs a list value");
            }

            return;
        }

        foreach (var child in c.All ?? c.Any ?? [c.Not!])
        {
            ValidateCondition(child, owner, fields, where, problems, depth + 1);
        }
    }

    public static bool Evaluate(Condition c, IReadOnlyDictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(values);
        if (c.All is { } all)
        {
            return all.All(x => Evaluate(x, values));
        }

        if (c.Any is { } any)
        {
            return any.Any(x => Evaluate(x, values));
        }

        if (c.Not is { } not)
        {
            return !Evaluate(not, values);
        }

        var has = values.TryGetValue(c.Field!, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        return c.Op switch
        {
            "isSet" => has,
            "isNotSet" => !has,
            "equals" => has && JsonEquals(v, c.Value!.Value),
            "notEquals" => !has || !JsonEquals(v, c.Value!.Value),
            "in" => has && c.Value!.Value.EnumerateArray().Any(x => JsonEquals(v, x)),
            "notIn" => !has || !c.Value!.Value.EnumerateArray().Any(x => JsonEquals(v, x)),
            "greaterThan" => has && v.ValueKind == JsonValueKind.Number && c.Value!.Value.ValueKind == JsonValueKind.Number && v.GetDecimal() > c.Value.Value.GetDecimal(),
            "lessThan" => has && v.ValueKind == JsonValueKind.Number && c.Value!.Value.ValueKind == JsonValueKind.Number && v.GetDecimal() < c.Value.Value.GetDecimal(),
            _ => false,
        };
    }

    /// <summary>Validates submitted values: visibility, requiredness, types, ranges, options; no values for hidden or unknown fields.</summary>
    public static IReadOnlyDictionary<string, string> ValidateValues(FormDefinition form, IReadOnlyDictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(values);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in values.Keys.Where(k => form.Fields.All(f => f.Id != k)))
        {
            errors[key] = "unknown field";
        }

        foreach (var f in form.Fields)
        {
            var visible = f.VisibleWhen is null || Evaluate(f.VisibleWhen, values);
            var has = values.TryGetValue(f.Id, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            if (!visible)
            {
                if (has)
                {
                    errors[f.Id] = "not applicable";
                }

                continue;
            }

            var required = f.Required || (f.RequiredWhen is not null && Evaluate(f.RequiredWhen, values));
            if (!has)
            {
                if (required)
                {
                    errors[f.Id] = "required";
                }

                continue;
            }

            if (CheckValue(f, v) is { } problem)
            {
                errors[f.Id] = problem;
            }
        }

        return errors;
    }

    private static string? CheckValue(FieldDefinition f, JsonElement v)
    {
        switch (f.Type)
        {
            case FieldType.Text or FieldType.TextArea:
                if (v.ValueKind != JsonValueKind.String)
                {
                    return "must be text";
                }

                return f.MaxLength is { } ml && v.GetString()!.Length > ml ? $"at most {ml} characters" : null;
            case FieldType.Integer or FieldType.Decimal:
                if (v.ValueKind != JsonValueKind.Number || (f.Type == FieldType.Integer && !v.TryGetInt64(out _)))
                {
                    return f.Type == FieldType.Integer ? "must be a whole number" : "must be a number";
                }

                var n = v.GetDecimal();
                if (f.Min is { } lo && n < lo)
                {
                    return $"at least {lo.ToString(CultureInfo.InvariantCulture)}{(f.Unit is null ? "" : " " + f.Unit)}";
                }

                return f.Max is { } hi && n > hi ? $"at most {hi.ToString(CultureInfo.InvariantCulture)}{(f.Unit is null ? "" : " " + f.Unit)}" : null;
            case FieldType.Date:
                return v.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : "must be a date (YYYY-MM-DD)";
            case FieldType.Boolean:
                return v.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be yes or no";
            case FieldType.Choice:
                return v.ValueKind == JsonValueKind.String && f.Options.Contains(v.GetString()!) ? null : "not an allowed option";
            case FieldType.MultiChoice:
                return v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && f.Options.Contains(x.GetString()!))
                    ? null
                    : "not an allowed option";
            default:
                return "unsupported type";
        }
    }

    private static bool JsonEquals(JsonElement a, JsonElement b) =>
        a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number
            ? a.GetDecimal() == b.GetDecimal()
            : a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText();
}
