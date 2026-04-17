namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Represents the state of a task queue.
/// </summary>
public enum TaskQueueState
{
    Idle,
    Running,
    Blocked,
    Completed
}

/// <summary>
/// Event arguments for task queue events.
/// </summary>
public record TaskQueueEvent
{
    public required string QueueId { get; init; }
    public required TaskQueueEventType EventType { get; init; }
    public string? IssueId { get; init; }
    public string? Error { get; init; }
    public TaskQueueState? PreviousState { get; init; }
    public TaskQueueState? NewState { get; init; }
}

/// <summary>
/// Types of events emitted by a task queue.
/// </summary>
public enum TaskQueueEventType
{
    IssueStarted,
    IssueCompleted,
    IssueFailed,
    StateChanged
}

/// <summary>
/// Record of a completed issue execution.
/// </summary>
public record TaskQueueHistoryEntry
{
    public required string IssueId { get; init; }
    public required AgentStartRequest Request { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Represents a single sequential execution pipeline for processing issues.
/// </summary>
public interface ITaskQueue
{
    /// <summary>
    /// The unique identifier for this queue.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The current state of the queue.
    /// </summary>
    TaskQueueState State { get; }

    /// <summary>
    /// The currently executing issue request, or null if idle.
    /// </summary>
    AgentStartRequest? CurrentRequest { get; }

    /// <summary>
    /// The ordered list of pending issue requests.
    /// </summary>
    IReadOnlyList<AgentStartRequest> PendingRequests { get; }

    /// <summary>
    /// The history of completed issue executions.
    /// </summary>
    IReadOnlyList<TaskQueueHistoryEntry> History { get; }

    /// <summary>
    /// Enqueues an issue request for processing. Starts processing if the queue is idle.
    /// </summary>
    Task EnqueueAsync(AgentStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a pending issue request from the queue by issue ID.
    /// Returns true if the request was found and removed.
    /// </summary>
    bool Dequeue(string issueId);

    /// <summary>
    /// Pauses the queue, preventing new issues from starting after the current one completes.
    /// The queue transitions to Idle state.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes the queue after a pause, continuing to process pending issues.
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all pending work and transitions the queue to Completed state.
    /// Does not cancel the currently executing issue.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Notifies the queue that a blocking dependency has been resolved,
    /// allowing it to transition from Blocked to Running.
    /// </summary>
    Task UnblockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when queue events occur.
    /// </summary>
    event Action<TaskQueueEvent>? OnEvent;
}
