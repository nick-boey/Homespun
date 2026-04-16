using System.Text.RegularExpressions;
using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Parses an OpenSpec <c>tasks.md</c> file into a <see cref="TaskStateSummary"/>,
/// grouping tasks under their <c>## Heading</c> phase block.
/// </summary>
public static partial class TasksParser
{
    [GeneratedRegex(@"^##\s+(.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex PhaseHeadingRegex();

    [GeneratedRegex(@"^\s*-\s+\[([ xX])\]\s+(.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex TaskLineRegex();

    /// <summary>
    /// Parses tasks.md content. Returns <see cref="TaskStateSummary.Empty"/> for empty or
    /// tasks-less content.
    /// </summary>
    public static TaskStateSummary Parse(string? tasksMarkdown)
    {
        if (string.IsNullOrWhiteSpace(tasksMarkdown))
        {
            return TaskStateSummary.Empty;
        }

        var phases = new List<PhaseBuilder>();
        PhaseBuilder? current = null;
        string? nextIncomplete = null;
        var done = 0;
        var total = 0;

        foreach (var rawLine in tasksMarkdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var phaseMatch = PhaseHeadingRegex().Match(line);
            if (phaseMatch.Success)
            {
                current = new PhaseBuilder(phaseMatch.Groups[1].Value);
                phases.Add(current);
                continue;
            }

            var taskMatch = TaskLineRegex().Match(line);
            if (!taskMatch.Success)
            {
                continue;
            }

            var marker = taskMatch.Groups[1].Value;
            var description = taskMatch.Groups[2].Value;
            var isDone = marker is "x" or "X";

            total++;
            if (isDone) done++;
            else nextIncomplete ??= description;

            // Tasks without a preceding phase heading go into an "(unnamed)" bucket so counts reconcile.
            current ??= new PhaseBuilder("(unnamed)");
            if (current.Tasks.Count == 0 && !phases.Contains(current))
            {
                phases.Add(current);
            }

            current.Tasks.Add(new PhaseTask { Description = description, Done = isDone });
            if (isDone) current.Done++;
            current.Total++;
        }

        return new TaskStateSummary
        {
            TasksDone = done,
            TasksTotal = total,
            NextIncomplete = nextIncomplete,
            Phases = phases
                .Where(p => p.Total > 0)
                .Select(p => new PhaseState
                {
                    Name = p.Name,
                    Done = p.Done,
                    Total = p.Total,
                    Tasks = p.Tasks
                })
                .ToList()
        };
    }

    private sealed class PhaseBuilder(string name)
    {
        public string Name { get; } = name;
        public int Done { get; set; }
        public int Total { get; set; }
        public List<PhaseTask> Tasks { get; } = new();
    }
}
