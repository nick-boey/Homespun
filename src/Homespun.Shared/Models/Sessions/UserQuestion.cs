namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Represents a question option from the AskUserQuestion tool.
/// </summary>
public class QuestionOption
{
    /// <summary>
    /// The display text for this option.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Description of what this option means.
    /// </summary>
    public required string Description { get; init; }
}

/// <summary>
/// Represents a single question from the AskUserQuestion tool.
/// </summary>
public class UserQuestion
{
    /// <summary>
    /// The full question text to display.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Short label for the question (max 12 characters).
    /// </summary>
    public required string Header { get; init; }

    /// <summary>
    /// Available choices for this question (2-4 options).
    /// </summary>
    public required List<QuestionOption> Options { get; init; }

    /// <summary>
    /// Whether multiple options can be selected.
    /// </summary>
    public bool MultiSelect { get; init; }
}

/// <summary>
/// Represents a pending question from Claude that needs user input.
/// </summary>
public class PendingQuestion
{
    /// <summary>
    /// Unique identifier for this pending question.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The tool use ID from the Claude message that triggered this question.
    /// </summary>
    public required string ToolUseId { get; init; }

    /// <summary>
    /// The questions to ask the user.
    /// </summary>
    public required List<UserQuestion> Questions { get; init; }

    /// <summary>
    /// When the question was received.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an answer to a user question.
/// </summary>
public class QuestionAnswer
{
    /// <summary>
    /// The question text that was answered.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// The selected option label(s), comma-separated for multi-select, or custom text.
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// Whether this was a custom "Other" answer.
    /// </summary>
    public bool IsCustomAnswer { get; init; }
}
