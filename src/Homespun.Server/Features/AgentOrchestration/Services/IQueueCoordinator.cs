namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Status of a queue coordinator's execution for a project.
/// </summary>
public enum QueueCoordinatorStatus
{
    Idle,
    Running,
    Completed,
    Cancelled
}

/// <summary>
/// Snapshot of the coordinator's state for a project.
/// </summary>
public record QueueCoordinatorState
{
    public required string ProjectId { get; init; }
    public required QueueCoordinatorStatus Status { get; init; }
    public required IReadOnlyList<ITaskQueue> ActiveQueues { get; init; }
    public required int MaxConcurrency { get; init; }
    public required int RunningQueueCount { get; init; }
    public string? RootIssueId { get; init; }
}

/// <summary>
/// Coordinates multiple TaskQueues for a project, spawning queues based on
/// the issue hierarchy's execution modes (Series vs Parallel).
/// </summary>
public interface IQueueCoordinator
{
    /// <summary>
    /// Starts execution from a root issue, creating queues based on the issue's
    /// execution mode and children.
    /// </summary>
    Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, CancellationToken ct = default);

    /// <summary>
    /// Starts execution from a root issue with per-issue-type workflow mappings.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="issueId">The root issue ID to start from.</param>
    /// <param name="projectPath">The local project path.</param>
    /// <param name="defaultBranch">The default git branch.</param>
    /// <param name="workflowMappings">Mapping of issue type to workflow ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, Dictionary<string, string> workflowMappings, CancellationToken ct = default);

    /// <summary>
    /// Gets all active queues for a project.
    /// </summary>
    IReadOnlyList<ITaskQueue> GetActiveQueues(string projectId);

    /// <summary>
    /// Cancels all queues for a project.
    /// </summary>
    void CancelAll(string projectId);

    /// <summary>
    /// Gets the current coordination status for a project.
    /// </summary>
    QueueCoordinatorState? GetStatus(string projectId);

    /// <summary>
    /// Event raised when coordination-level events occur (queue created, all queues complete, etc.).
    /// </summary>
    event Action<QueueCoordinatorEvent>? OnEvent;
}

/// <summary>
/// Events emitted by the queue coordinator.
/// </summary>
public record QueueCoordinatorEvent
{
    public required string ProjectId { get; init; }
    public required QueueCoordinatorEventType EventType { get; init; }
    public string? QueueId { get; init; }
    public string? IssueId { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Types of events emitted by the coordinator.
/// </summary>
public enum QueueCoordinatorEventType
{
    QueueCreated,
    QueueCompleted,
    AllQueuesCompleted,
    ExecutionStarted,
    ExecutionCancelled,
    ExecutionFailed
}
