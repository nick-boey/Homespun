using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing Claude Code sessions.
/// </summary>
public interface IClaudeSessionService
{
    /// <summary>
    /// Starts a new Claude Code session for an entity.
    /// </summary>
    Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a previously saved session using Claude's --resume flag.
    /// </summary>
    /// <param name="sessionId">Claude's session UUID (from the .jsonl filename)</param>
    /// <param name="entityId">Our PR/issue ID</param>
    /// <param name="projectId">Our project ID</param>
    /// <param name="workingDirectory">The worktree path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new active session that will resume the previous conversation</returns>
    Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all resumable sessions for an entity (PR/issue).
    /// Discovers from Claude's storage and enriches with our metadata.
    /// </summary>
    /// <param name="entityId">Our PR/issue ID</param>
    /// <param name="workingDirectory">The worktree path to scan for sessions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of resumable sessions ordered by last activity (newest first)</returns>
    Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session.
    /// </summary>
    Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session with a specific permission mode.
    /// </summary>
    Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session with a specific permission mode and model.
    /// </summary>
    Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, string? model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the conversation context for a session.
    /// Messages are kept visible but the AI will start fresh.
    /// </summary>
    Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a pending plan by optionally clearing context and sending the plan content as a message.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="clearContext">Whether to clear conversation context before execution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an existing session.
    /// </summary>
    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    ClaudeSession? GetSession(string sessionId);

    /// <summary>
    /// Gets a session by entity ID.
    /// </summary>
    ClaudeSession? GetSessionByEntityId(string entityId);

    /// <summary>
    /// Gets all sessions for a project.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetAllSessions();

    /// <summary>
    /// Answers a pending question from Claude.
    /// This will resume the session by providing the answers to the AskUserQuestion tool.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="answers">Dictionary mapping question text to selected answer text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached messages for a session from the message cache store.
    /// Returns messages from the persistent JSONL cache, not the in-memory store.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of cached messages in chronological order</returns>
    Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session cache summaries for an entity (issue/PR).
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="entityId">The entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of session summaries</returns>
    Task<IReadOnlyList<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId,
        string entityId,
        CancellationToken cancellationToken = default);
}
