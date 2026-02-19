using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines server-to-client SignalR messages for the Claude Code hub.
/// </summary>
public interface IClaudeCodeHubClient
{
    Task SessionStarted(ClaudeSession session);
    Task SessionStopped(string sessionId);
    Task SessionState(ClaudeSession session);
    Task MessageReceived(ClaudeMessage message);
    Task ContentBlockReceived(ClaudeMessageContent content);
    Task SessionStatusChanged(string sessionId, ClaudeSessionStatus status);

    /// <summary>
    /// Notifies clients when a session's mode or model changes.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="mode">The new session mode (Plan or Build)</param>
    /// <param name="model">The current model being used</param>
    Task SessionModeModelChanged(string sessionId, SessionMode mode, string model);

    Task SessionResultReceived(string sessionId, decimal totalCostUsd, long durationMs);
    Task StreamingContentStarted(ClaudeMessageContent content, int index);
    Task StreamingContentDelta(ClaudeMessageContent content, string delta, int index);
    Task StreamingContentStopped(ClaudeMessageContent content, int index);
    Task QuestionReceived(PendingQuestion question);
    Task QuestionAnswered();
    Task ContextCleared(string sessionId);

    /// <summary>
    /// Notifies clients when a session encounters an error.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="errorMessage">User-friendly error message</param>
    /// <param name="errorSubtype">SDK error subtype (e.g., error_max_turns, error_during_execution)</param>
    /// <param name="isRecoverable">Whether the session can be resumed by sending another message</param>
    Task SessionError(string sessionId, string errorMessage, string? errorSubtype, bool isRecoverable);
}
