namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Defines the contract for queuing issue write operations for asynchronous persistence.
/// </summary>
public interface IIssueSerializationQueue
{
    /// <summary>
    /// Enqueues an issue write operation for asynchronous persistence to disk.
    /// </summary>
    /// <param name="operation">The write operation to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask EnqueueAsync(IssueWriteOperation operation, CancellationToken ct = default);

    /// <summary>
    /// Gets the number of pending operations in the queue.
    /// </summary>
    int PendingCount { get; }

    /// <summary>
    /// Gets whether the queue is currently processing operations.
    /// </summary>
    bool IsProcessing { get; }
}

/// <summary>
/// Represents a write operation to be performed on an issue.
/// </summary>
public enum WriteOperationType
{
    Create,
    Update,
    Delete
}

/// <summary>
/// Encapsulates a queued issue write operation with all data needed for persistence.
/// </summary>
/// <param name="ProjectPath">The path to the project containing the .fleece/ directory.</param>
/// <param name="IssueId">The ID of the issue being written.</param>
/// <param name="Type">The type of write operation.</param>
/// <param name="WriteAction">The async action that performs the actual Fleece.Core persistence.</param>
/// <param name="QueuedAt">When the operation was enqueued.</param>
public record IssueWriteOperation(
    string ProjectPath,
    string IssueId,
    WriteOperationType Type,
    Func<CancellationToken, Task> WriteAction,
    DateTimeOffset QueuedAt
);
