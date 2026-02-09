namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Information about a running Claude Code session for display.
/// </summary>
public class RunningSessionInfo
{
    /// <summary>
    /// The session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The entity ID this session is for.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The working directory path.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The model being used.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The session mode.
    /// </summary>
    public required SessionMode Mode { get; init; }

    /// <summary>
    /// Current status of the session.
    /// </summary>
    public required ClaudeSessionStatus Status { get; init; }

    /// <summary>
    /// When the session started.
    /// </summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// URL for the chat UI (route to /session/{id}).
    /// </summary>
    public string ChatUrl => $"/session/{SessionId}";
}
