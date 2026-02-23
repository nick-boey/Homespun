namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Represents a single entry in the issue history.
/// </summary>
public class IssueHistoryEntry
{
    /// <summary>
    /// Timestamp of when this history entry was created (ISO 8601 format).
    /// </summary>
    public required string Timestamp { get; set; }

    /// <summary>
    /// Type of operation that created this history entry.
    /// Values: Create, Update, Delete, AddParent, RemoveParent
    /// </summary>
    public required string OperationType { get; set; }

    /// <summary>
    /// ID of the issue affected by this operation, if applicable.
    /// </summary>
    public string? IssueId { get; set; }

    /// <summary>
    /// Human-readable description of the operation.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Represents the current state of the issue history for a project.
/// </summary>
public class IssueHistoryState
{
    /// <summary>
    /// The timestamp of the currently active history point.
    /// Null if no history exists.
    /// </summary>
    public string? CurrentTimestamp { get; set; }

    /// <summary>
    /// Whether undo is available (there are earlier history entries).
    /// </summary>
    public bool CanUndo { get; set; }

    /// <summary>
    /// Whether redo is available (there are later history entries after the current position).
    /// </summary>
    public bool CanRedo { get; set; }

    /// <summary>
    /// Total number of history entries.
    /// </summary>
    public int TotalEntries { get; set; }

    /// <summary>
    /// Description of the last operation that can be undone.
    /// </summary>
    public string? UndoDescription { get; set; }

    /// <summary>
    /// Description of the operation that can be redone.
    /// </summary>
    public string? RedoDescription { get; set; }
}

/// <summary>
/// Response model for undo/redo operations.
/// </summary>
public class IssueHistoryOperationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The new history state after the operation.
    /// </summary>
    public IssueHistoryState? State { get; set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
