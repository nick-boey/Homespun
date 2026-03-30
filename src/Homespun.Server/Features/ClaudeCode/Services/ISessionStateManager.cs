namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Manages thread-safe in-memory state for Claude sessions.
/// Owns all ConcurrentDictionary fields previously held by ClaudeSessionService.
/// </summary>
public interface ISessionStateManager
{
    // --- CancellationTokenSource management ---
    CancellationTokenSource GetOrCreateCts(string sessionId);
    CancellationTokenSource? GetCts(string sessionId);
    bool TryRemoveCts(string sessionId, out CancellationTokenSource? cts);
    Task CancelAndRemoveCtsAsync(string sessionId);

    // --- Run ID management (AG-UI) ---
    string? GetRunId(string sessionId);
    void SetRunId(string sessionId, string runId);
    void RemoveRunId(string sessionId);

    // --- Turn ID management ---
    Guid? GetCurrentTurnId(string sessionId);
    void SetCurrentTurnId(string sessionId, Guid turnId);
    bool IsTurnActive(string sessionId, Guid turnId);
    void RemoveTurnId(string sessionId);

    // --- Agent session ID management ---
    string? GetAgentSessionId(string sessionId);
    void SetAgentSessionId(string sessionId, string agentSessionId);
    bool TryRemoveAgentSessionId(string sessionId, out string? agentSessionId);

    // --- Tool use tracking ---
    System.Collections.Concurrent.ConcurrentDictionary<string, string> GetOrCreateSessionToolUses(string sessionId);
    void RemoveSessionToolUses(string sessionId);

    // --- Question answer sources ---
    void SetQuestionAnswerSource(string sessionId, TaskCompletionSource<Dictionary<string, string>> tcs);
    bool TryGetQuestionAnswerSource(string sessionId, out TaskCompletionSource<Dictionary<string, string>>? tcs);
    bool TryRemoveQuestionAnswerSource(string sessionId);

    // --- Bulk cleanup ---
    void CleanupSession(string sessionId);
    Task CleanupAllAsync();
}
