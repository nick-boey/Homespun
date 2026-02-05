namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents a todo item parsed from a TodoWrite tool call in a Claude session.
/// </summary>
public class SessionTodoItem
{
    /// <summary>
    /// The task description (imperative form, e.g., "Run tests").
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The active form shown during execution (e.g., "Running tests").
    /// </summary>
    public required string ActiveForm { get; init; }

    /// <summary>
    /// The current status of this todo item.
    /// </summary>
    public required TodoStatus Status { get; init; }
}

/// <summary>
/// Status values for a todo item, matching the TodoWrite tool schema.
/// </summary>
public enum TodoStatus
{
    /// <summary>
    /// Task not yet started.
    /// </summary>
    Pending,

    /// <summary>
    /// Currently working on this task.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "in_progress")]
    InProgress,

    /// <summary>
    /// Task finished successfully.
    /// </summary>
    Completed
}
