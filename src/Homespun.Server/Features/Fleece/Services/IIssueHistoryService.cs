using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for managing issue history, enabling undo/redo functionality.
/// Persists full issue snapshots as timestamped JSONL files in the .fleece/history/ folder.
/// </summary>
public interface IIssueHistoryService
{
    /// <summary>
    /// Maximum number of history entries to keep. Oldest entries are pruned when exceeded.
    /// </summary>
    const int MaxHistoryEntries = 100;

    /// <summary>
    /// Records a snapshot of the current issues state after a change.
    /// If the current position is not at the latest history entry, future entries are truncated first.
    /// </summary>
    /// <param name="projectPath">Path to the project's local repository.</param>
    /// <param name="issues">The complete list of issues to snapshot.</param>
    /// <param name="operationType">Type of operation (Create, Update, Delete, AddParent, RemoveParent).</param>
    /// <param name="issueId">ID of the affected issue, if applicable.</param>
    /// <param name="description">Human-readable description of the operation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordSnapshotAsync(
        string projectPath,
        IReadOnlyList<Issue> issues,
        string operationType,
        string? issueId,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Undoes the last change by moving to the previous history entry.
    /// </summary>
    /// <param name="projectPath">Path to the project's local repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of issues from the previous history state, or null if undo is not available.</returns>
    Task<IReadOnlyList<Issue>?> UndoAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Redoes a previously undone change by moving to the next history entry.
    /// </summary>
    /// <param name="projectPath">Path to the project's local repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of issues from the next history state, or null if redo is not available.</returns>
    Task<IReadOnlyList<Issue>?> RedoAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Gets the current history state for a project.
    /// </summary>
    /// <param name="projectPath">Path to the project's local repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current history state including undo/redo availability.</returns>
    Task<IssueHistoryState> GetStateAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Checks if the current position is at the latest history entry.
    /// Used to determine if future history should be truncated before recording a new snapshot.
    /// </summary>
    /// <param name="projectPath">Path to the project's local repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if at the latest entry (or no history exists), false otherwise.</returns>
    Task<bool> IsAtLatestAsync(string projectPath, CancellationToken ct = default);
}
