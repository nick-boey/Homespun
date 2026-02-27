using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a session.
/// </summary>
public class CreateSessionRequest
{
    /// <summary>
    /// The entity ID (e.g., issue ID, PR ID).
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The session mode.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Plan;

    /// <summary>
    /// The Claude model to use (defaults to project's default model).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Working directory (defaults to project local path).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Request model for sending a message.
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// The message to send.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The session mode for this message. Defaults to Build.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Build;
}

/// <summary>
/// Request model for resuming a session.
/// </summary>
public class ResumeSessionRequest
{
    /// <summary>
    /// The entity ID (e.g., issue ID, PR ID).
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Working directory for the session.
    /// </summary>
    public required string WorkingDirectory { get; set; }
}
