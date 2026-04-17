namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// The aggregated checkbox state of a change's <c>tasks.md</c> file, grouped by <c>## N. Phase</c> headings.
/// </summary>
public class TaskStateSummary
{
    /// <summary>
    /// Total tasks marked <c>[x]</c>.
    /// </summary>
    public int TasksDone { get; init; }

    /// <summary>
    /// Total tasks regardless of status (<c>[ ]</c> + <c>[x]</c>).
    /// </summary>
    public int TasksTotal { get; init; }

    /// <summary>
    /// Per-phase roll-up. Phases are <c>## Heading</c> blocks in <c>tasks.md</c>.
    /// </summary>
    public List<PhaseState> Phases { get; init; } = new();

    /// <summary>
    /// Description of the first unchecked task in source order, or <c>null</c> when every task is done.
    /// </summary>
    public string? NextIncomplete { get; init; }

    /// <summary>
    /// Empty summary (no tasks.md found or file had no tasks).
    /// </summary>
    public static TaskStateSummary Empty { get; } = new();
}

/// <summary>
/// Roll-up for one <c>## Heading</c> block in <c>tasks.md</c>.
/// </summary>
public class PhaseState
{
    public required string Name { get; init; }
    public int Done { get; init; }
    public int Total { get; init; }
    public List<PhaseTask> Tasks { get; init; } = new();
}

/// <summary>
/// Individual checkbox task under a phase.
/// </summary>
public class PhaseTask
{
    public required string Description { get; init; }
    public bool Done { get; init; }
}
