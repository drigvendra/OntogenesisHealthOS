using System.Text.Json;
using HealthOS.Modules.Sdk.Configuration;

namespace HealthOS.Modules.Sdk.Tests;

public class FormEngineTests
{
    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    private static readonly FormDefinition Intake = new()
    {
        Code = "dental-intake",
        Title = "Dental intake",
        Fields =
        [
            new FieldDefinition { Id = "painLevel", Label = "Pain (0-10)", Type = FieldType.Integer, Required = true, Min = 0, Max = 10 },
            new FieldDefinition { Id = "painLocation", Label = "Where", Type = FieldType.Choice, Options = ["upper", "lower"],
                VisibleWhen = new Condition { Field = "painLevel", Op = "greaterThan", Value = J(0) }, RequiredWhen = new Condition { Field = "painLevel", Op = "greaterThan", Value = J(5) } },
            new FieldDefinition { Id = "pregnant", Label = "Pregnant", Type = FieldType.Boolean, Classification = DataClass.Phi },
            new FieldDefinition { Id = "lastVisit", Label = "Last visit", Type = FieldType.Date },
        ],
    };

    private static Dictionary<string, JsonElement> V(params (string, object)[] values) => values.ToDictionary(v => v.Item1, v => J(v.Item2));

    [Fact]
    public void Valid_definition_has_no_problems() => Assert.Empty(FormEngine.ValidateDefinition(Intake));

    [Fact]
    public void Definition_problems_are_reported()
    {
        var bad = Intake with
        {
            Code = "Bad Code",
            Fields =
            [
                new FieldDefinition { Id = "a", Label = "A", Type = FieldType.Choice },
                new FieldDefinition { Id = "a", Label = "", Type = FieldType.Text, Min = 1 },
                new FieldDefinition { Id = "b", Label = "B", Type = FieldType.Text, VisibleWhen = new Condition { Field = "missing", Op = "equals", Value = J(1) } },
                new FieldDefinition { Id = "c", Label = "C", Type = FieldType.Text, VisibleWhen = new Condition { Field = "c", Op = "frobnicate" } },
            ],
        };
        var problems = FormEngine.ValidateDefinition(bad);
        Assert.Contains(problems, p => p.StartsWith("code", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("need options", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("duplicate id", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("min/max only apply", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown field 'missing'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("depend on itself", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("operator must be", StringComparison.Ordinal));
    }

    [Fact]
    public void Conditional_visibility_and_requiredness_are_applied()
    {
        Assert.Empty(FormEngine.ValidateValues(Intake, V(("painLevel", 0))));
        Assert.Equal("not applicable", FormEngine.ValidateValues(Intake, V(("painLevel", 0), ("painLocation", "upper")))["painLocation"]);
        Assert.Empty(FormEngine.ValidateValues(Intake, V(("painLevel", 3))));
        Assert.Equal("required", FormEngine.ValidateValues(Intake, V(("painLevel", 8)))["painLocation"]);
    }

    [Fact]
    public void Types_ranges_options_and_unknown_fields_are_checked()
    {
        var errors = FormEngine.ValidateValues(Intake, V(("painLevel", 11), ("painLocation", "middle"), ("pregnant", "yes"), ("lastVisit", "19/11/2026"), ("extra", 1)));
        Assert.Equal("at most 10", errors["painLevel"]);
        Assert.Equal("not an allowed option", errors["painLocation"]);
        Assert.Equal("must be yes or no", errors["pregnant"]);
        Assert.Equal("must be a date (YYYY-MM-DD)", errors["lastVisit"]);
        Assert.Equal("unknown field", errors["extra"]);
        Assert.Equal("required", FormEngine.ValidateValues(Intake, V())["painLevel"]);
    }

    [Fact]
    public void Composite_conditions_evaluate()
    {
        var c = new Condition { Any = [new Condition { Field = "x", Op = "in", Value = J(new[] { 1, 2 }) }, new Condition { Not = new Condition { Field = "y", Op = "isSet" } }] };
        Assert.True(FormEngine.Evaluate(c, V(("x", 2), ("y", true))));
        Assert.True(FormEngine.Evaluate(c, V(("x", 9))));
        Assert.False(FormEngine.Evaluate(c, V(("x", 9), ("y", true))));
    }
}

public class WorkflowValidatorTests
{
    private static readonly WorkflowDefinition SopApproval = new()
    {
        Code = "sop-approval",
        Title = "SOP approval",
        InitialState = "draft",
        States =
        [
            new WorkflowState { Id = "draft", Label = "Draft" },
            new WorkflowState { Id = "review", Label = "In review", SlaHours = 72 },
            new WorkflowState { Id = "approved", Label = "Approved", Terminal = true },
            new WorkflowState { Id = "withdrawn", Label = "Withdrawn", Terminal = true },
        ],
        Transitions =
        [
            new WorkflowTransition { Id = "submit", From = "draft", To = "review", Label = "Submit" },
            new WorkflowTransition { Id = "approve", From = "review", To = "approved", Label = "Approve", Permission = "careops.document.approve", RequiresESignature = true, RequiresDifferentActor = true },
            new WorkflowTransition { Id = "reject", From = "review", To = "draft", Label = "Send back" },
            new WorkflowTransition { Id = "withdraw", From = "draft", To = "withdrawn", Label = "Withdraw" },
        ],
    };

    [Fact]
    public void Valid_workflow_has_no_problems() =>
        Assert.Empty(WorkflowValidator.Validate(SopApproval, p => p == "careops.document.approve"));

    [Fact]
    public void Structural_problems_are_reported()
    {
        var bad = SopApproval with
        {
            InitialState = "nowhere",
            States = [.. SopApproval.States, new WorkflowState { Id = "orphan", Label = "Orphan" }],
            Transitions = [.. SopApproval.Transitions, new WorkflowTransition { Id = "reopen", From = "approved", To = "draft", Label = "Reopen" }],
        };
        var problems = WorkflowValidator.Validate(bad, _ => false);
        Assert.Contains(problems, p => p.Contains("initial state", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("leaves terminal state", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown permission", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreachable_states_and_missing_terminals_are_reported()
    {
        var bad = SopApproval with { States = [.. SopApproval.States, new WorkflowState { Id = "orphan", Label = "Orphan" }] };
        Assert.Contains(WorkflowValidator.Validate(bad), p => p == "state 'orphan' is unreachable");

        var loop = new WorkflowDefinition
        {
            Code = "loop", Title = "Loop", InitialState = "a",
            States = [new WorkflowState { Id = "a", Label = "A" }, new WorkflowState { Id = "b", Label = "B" }, new WorkflowState { Id = "end", Label = "End", Terminal = true }],
            Transitions = [new WorkflowTransition { Id = "ab", From = "a", To = "b", Label = "AB" }, new WorkflowTransition { Id = "ba", From = "b", To = "a", Label = "BA" }],
        };
        var problems = WorkflowValidator.Validate(loop);
        Assert.Contains(problems, p => p == "no terminal state is reachable");
    }
}
