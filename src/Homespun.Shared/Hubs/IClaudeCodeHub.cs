using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines client-to-server SignalR messages for the Claude Code hub.
/// </summary>
public interface IClaudeCodeHub
{
    Task JoinSession(string sessionId);
    Task LeaveSession(string sessionId);
    Task SendMessage(string sessionId, string message, PermissionMode permissionMode = PermissionMode.BypassPermissions);
    Task StopSession(string sessionId);
    Task InterruptSession(string sessionId);
    IReadOnlyList<ClaudeSession> GetAllSessions();
    IReadOnlyList<ClaudeSession> GetProjectSessions(string projectId);
    ClaudeSession? GetSession(string sessionId);
    Task AnswerQuestion(string sessionId, string answersJson);
    Task ExecutePlan(string sessionId, bool clearContext = true);
    Task<int> GetCachedMessageCount(string sessionId);

    /// <summary>
    /// Restart the container for a session and prepare for resumption.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The updated session, or null if not found</returns>
    Task<ClaudeSession?> RestartSession(string sessionId);
}
