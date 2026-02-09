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
    Task SessionResultReceived(string sessionId, decimal totalCostUsd, long durationMs);
    Task StreamingContentStarted(ClaudeMessageContent content, int index);
    Task StreamingContentDelta(ClaudeMessageContent content, string delta, int index);
    Task StreamingContentStopped(ClaudeMessageContent content, int index);
    Task QuestionReceived(PendingQuestion question);
    Task QuestionAnswered();
    Task ContextCleared(string sessionId);
}
