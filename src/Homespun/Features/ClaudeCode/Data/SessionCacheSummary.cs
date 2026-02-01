namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Summary information about a cached session.
/// </summary>
/// <param name="SessionId">The session ID</param>
/// <param name="EntityId">The entity ID (issue/PR)</param>
/// <param name="ProjectId">The project ID</param>
/// <param name="MessageCount">Number of messages in the cache</param>
/// <param name="CreatedAt">When the session was created</param>
/// <param name="LastMessageAt">When the last message was added</param>
/// <param name="Mode">The session mode</param>
/// <param name="Model">The model used</param>
public record SessionCacheSummary(
    string SessionId,
    string EntityId,
    string ProjectId,
    int MessageCount,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    SessionMode? Mode,
    string? Model
);
