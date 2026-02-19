namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Represents an active Claude Code session.
/// </summary>
public class ClaudeSession
{
    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The entity ID this session is associated with (e.g., BeadsIssue ID, PR ID).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The project ID this session belongs to.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The working directory for this session.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The Claude model being used for this session.
    /// Can be updated when a different model is used for a message.
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// The session mode (Plan or Build).
    /// Can be updated when permission mode changes (e.g., Plan to Build after plan approval).
    /// </summary>
    public required SessionMode Mode { get; set; }

    /// <summary>
    /// Current status of the session.
    /// </summary>
    public ClaudeSessionStatus Status { get; set; } = ClaudeSessionStatus.Starting;

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the session was last active.
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Messages exchanged in this session.
    /// </summary>
    public List<ClaudeMessage> Messages { get; init; } = [];

    /// <summary>
    /// Optional error message if the session is in error state.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The conversation ID from the Claude SDK, if available.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Optional system prompt for the session.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Total cost in USD for this session.
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// Total duration in milliseconds for this session.
    /// </summary>
    public long TotalDurationMs { get; set; }

    /// <summary>
    /// The pending question that needs a user answer (only set when Status is WaitingForQuestionAnswer).
    /// </summary>
    public PendingQuestion? PendingQuestion { get; set; }

    /// <summary>
    /// The path to the plan file created when ExitPlanMode was called.
    /// </summary>
    public string? PlanFilePath { get; set; }

    /// <summary>
    /// Plan content extracted from ExitPlanMode for mock/archived sessions
    /// where the plan file doesn't exist on disk.
    /// </summary>
    public string? PlanContent { get; set; }

    /// <summary>
    /// Timestamps when context was cleared during the session.
    /// Used to display separators between conversation segments.
    /// </summary>
    public List<DateTime> ContextClearMarkers { get; init; } = [];
}
