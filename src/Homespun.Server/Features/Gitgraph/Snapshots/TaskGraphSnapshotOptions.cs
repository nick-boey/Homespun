namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// Configuration for the per-project task-graph snapshot store + background
/// refresher. Bound from the <c>TaskGraphSnapshot</c> configuration section.
/// </summary>
public sealed class TaskGraphSnapshotOptions
{
    public const string SectionName = "TaskGraphSnapshot";

    /// <summary>
    /// Master on/off switch. When disabled, <c>GraphController.GetTaskGraphData</c>
    /// falls back to the original synchronous compute path.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the background refresher iterates tracked project keys and
    /// recomputes their snapshot. Default 10 seconds.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Idle window (in minutes) after which a tracked entry is evicted from
    /// the store if no read traffic has touched it. Default 5 minutes.
    /// </summary>
    public int IdleEvictionMinutes { get; set; } = 5;
}
