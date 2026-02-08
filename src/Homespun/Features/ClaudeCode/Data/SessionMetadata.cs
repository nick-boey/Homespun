namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Metadata about a Claude session that maps it to our entities (PR/issue).
/// This is lightweight data we store to enrich discovered sessions.
/// </summary>
/// <param name="SessionId">Claude's session UUID (from filename)</param>
/// <param name="EntityId">Our PR or issue ID</param>
/// <param name="ProjectId">Our project ID</param>
/// <param name="WorkingDirectory">The clone path</param>
/// <param name="Mode">Session mode (Plan or Build)</param>
/// <param name="Model">Claude model used</param>
/// <param name="SystemPrompt">Optional system prompt</param>
/// <param name="CreatedAt">When the session was created</param>
public record SessionMetadata(
    string SessionId,
    string EntityId,
    string ProjectId,
    string WorkingDirectory,
    SessionMode Mode,
    string Model,
    string? SystemPrompt,
    DateTime CreatedAt
);
