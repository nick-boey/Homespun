using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Manages session lifecycle: start, resume, stop, interrupt, restart, dispose,
/// clone state checks, mode/model changes, and issue operations.
/// </summary>
public interface ISessionLifecycleService : IAsyncDisposable
{
    Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<ClaudeSession> StartSessionWithTerminationAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        bool terminateExisting,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    Task<ClaudeSession?> RestartSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default);

    Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ClaudeSession> ClearContextAndStartNewAsync(
        string currentSessionId,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default);

    Task SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default);

    Task SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default);

    Task<AgentStartCheckResult> CheckCloneStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<string> AcceptIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<string> CancelIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId,
        string entityId,
        CancellationToken cancellationToken = default);
}
