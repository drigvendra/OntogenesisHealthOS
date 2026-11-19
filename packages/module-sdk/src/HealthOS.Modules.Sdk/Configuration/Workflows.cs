using System.Text.RegularExpressions;

namespace HealthOS.Modules.Sdk.Configuration;

public sealed record WorkflowState
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public bool Terminal { get; init; }

    /// <summary>Target time in this state before escalation (enforced by the workflow engine, M4).</summary>
    public int? SlaHours { get; init; }
}

public sealed record WorkflowTransition
{
    public required string Id { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Label { get; init; }

    /// <summary>Permission code required to take this transition.</summary>
    public string? Permission { get; init; }

    /// <summary>Electronic signature (re-authentication + meaning) required — GxP approvals.</summary>
    public bool RequiresESignature { get; init; }

    /// <summary>The actor must differ from whoever took the previous transition (four eyes).</summary>
    public bool RequiresDifferentActor { get; init; }

    /// <summary>Declarative guard on the record's field values.</summary>
    public Condition? Guard { get; init; }
}

public sealed record WorkflowDefinition
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required string InitialState { get; init; }
    public required IReadOnlyList<WorkflowState> States { get; init; }
    public required IReadOnlyList<WorkflowTransition> Transitions { get; init; }
}

/// <summary>Static validation of workflow definitions (execution is the workflow engine, M4).</summary>
public static partial class WorkflowValidator
{
    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(WorkflowDefinition wf, Func<string, bool>? permissionExists = null)
    {
        ArgumentNullException.ThrowIfNull(wf);
        var problems = new List<string>();

        if (!IdPattern().IsMatch(wf.Code))
        {
            problems.Add("code: use 2-64 lowercase letters, digits or hyphens");
        }

        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in wf.States)
        {
            if (!IdPattern().IsMatch(s.Id))
            {
                problems.Add($"state '{s.Id}': invalid id");
            }

            if (!states.Add(s.Id))
            {
                problems.Add($"state '{s.Id}': duplicate");
            }

            if (s.SlaHours is <= 0)
            {
                problems.Add($"state '{s.Id}': SLA must be positive");
            }
        }

        if (!states.Contains(wf.InitialState))
        {
            problems.Add($"initial state '{wf.InitialState}' is not defined");
        }

        var transitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in wf.Transitions)
        {
            if (!transitionIds.Add(t.Id))
            {
                problems.Add($"transition '{t.Id}': duplicate");
            }

            if (!states.Contains(t.From) || !states.Contains(t.To))
            {
                problems.Add($"transition '{t.Id}': unknown state");
            }

            if (wf.States.FirstOrDefault(s => s.Id == t.From) is { Terminal: true })
            {
                problems.Add($"transition '{t.Id}': leaves terminal state '{t.From}'");
            }

            if (t.Permission is not null && permissionExists is not null && !permissionExists(t.Permission))
            {
                problems.Add($"transition '{t.Id}': unknown permission '{t.Permission}'");
            }
        }

        if (!wf.States.Any(s => s.Terminal))
        {
            problems.Add("at least one terminal state is required");
        }

        // Every state must be reachable from the initial state, and a terminal state must be reachable.
        if (states.Contains(wf.InitialState))
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal) { wf.InitialState };
            var queue = new Queue<string>([wf.InitialState]);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var t in wf.Transitions.Where(t => t.From == current))
                {
                    if (reachable.Add(t.To))
                    {
                        queue.Enqueue(t.To);
                    }
                }
            }

            problems.AddRange(wf.States.Where(s => !reachable.Contains(s.Id)).Select(s => $"state '{s.Id}' is unreachable"));
            if (!wf.States.Any(s => s.Terminal && reachable.Contains(s.Id)))
            {
                problems.Add("no terminal state is reachable");
            }
        }

        return problems;
    }
}
