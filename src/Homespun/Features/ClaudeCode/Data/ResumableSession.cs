namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// A session that can be resumed, combining discovered session data with our metadata.
/// Used by the UI to display previous sessions.
/// </summary>
/// <param name="SessionId">Claude's session UUID (used with --resume)</param>
/// <param name="LastActivityAt">Last activity timestamp (from file modification time)</param>
/// <param name="Mode">Session mode if we have metadata (may be null)</param>
/// <param name="Model">Model used if we have metadata (may be null)</param>
/// <param name="MessageCount">Estimated message count from JSONL (may be null)</param>
public record ResumableSession(
    string SessionId,
    DateTime LastActivityAt,
    SessionMode? Mode,
    string? Model,
    int? MessageCount
);
