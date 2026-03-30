using System.Collections.Concurrent;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Thread-safe in-memory state manager for Claude sessions.
/// </summary>
public class SessionStateManager : ISessionStateManager
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionCts = new();
    private readonly ConcurrentDictionary<string, string> _sessionRunIds = new();
    private readonly ConcurrentDictionary<string, Guid> _currentMessageTurnIds = new();
    private readonly ConcurrentDictionary<string, string> _agentSessionIds = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionToolUses = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Dictionary<string, string>>> _questionAnswerSources = new();

    // --- CancellationTokenSource management ---

    public CancellationTokenSource GetOrCreateCts(string sessionId)
    {
        return _sessionCts.GetOrAdd(sessionId, _ => new CancellationTokenSource());
    }

    public CancellationTokenSource? GetCts(string sessionId)
    {
        return _sessionCts.GetValueOrDefault(sessionId);
    }

    public bool TryRemoveCts(string sessionId, out CancellationTokenSource? cts)
    {
        return _sessionCts.TryRemove(sessionId, out cts);
    }

    public async Task CancelAndRemoveCtsAsync(string sessionId)
    {
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    // --- Run ID management ---

    public string? GetRunId(string sessionId)
    {
        return _sessionRunIds.GetValueOrDefault(sessionId);
    }

    public void SetRunId(string sessionId, string runId)
    {
        _sessionRunIds[sessionId] = runId;
    }

    public void RemoveRunId(string sessionId)
    {
        _sessionRunIds.TryRemove(sessionId, out _);
    }

    // --- Turn ID management ---

    public Guid? GetCurrentTurnId(string sessionId)
    {
        return _currentMessageTurnIds.TryGetValue(sessionId, out var turnId) ? turnId : null;
    }

    public void SetCurrentTurnId(string sessionId, Guid turnId)
    {
        _currentMessageTurnIds[sessionId] = turnId;
    }

    public bool IsTurnActive(string sessionId, Guid turnId)
    {
        return _currentMessageTurnIds.TryGetValue(sessionId, out var current) && current == turnId;
    }

    public void RemoveTurnId(string sessionId)
    {
        _currentMessageTurnIds.TryRemove(sessionId, out _);
    }

    // --- Agent session ID management ---

    public string? GetAgentSessionId(string sessionId)
    {
        return _agentSessionIds.GetValueOrDefault(sessionId);
    }

    public void SetAgentSessionId(string sessionId, string agentSessionId)
    {
        _agentSessionIds[sessionId] = agentSessionId;
    }

    public bool TryRemoveAgentSessionId(string sessionId, out string? agentSessionId)
    {
        return _agentSessionIds.TryRemove(sessionId, out agentSessionId);
    }

    // --- Tool use tracking ---

    public ConcurrentDictionary<string, string> GetOrCreateSessionToolUses(string sessionId)
    {
        return _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
    }

    public void RemoveSessionToolUses(string sessionId)
    {
        _sessionToolUses.TryRemove(sessionId, out _);
    }

    // --- Question answer sources ---

    public void SetQuestionAnswerSource(string sessionId, TaskCompletionSource<Dictionary<string, string>> tcs)
    {
        _questionAnswerSources[sessionId] = tcs;
    }

    public bool TryGetQuestionAnswerSource(string sessionId, out TaskCompletionSource<Dictionary<string, string>>? tcs)
    {
        return _questionAnswerSources.TryGetValue(sessionId, out tcs);
    }

    public bool TryRemoveQuestionAnswerSource(string sessionId)
    {
        return _questionAnswerSources.TryRemove(sessionId, out _);
    }

    // --- Bulk cleanup ---

    public void CleanupSession(string sessionId)
    {
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        _sessionRunIds.TryRemove(sessionId, out _);
        _currentMessageTurnIds.TryRemove(sessionId, out _);
        _agentSessionIds.TryRemove(sessionId, out _);
        _sessionToolUses.TryRemove(sessionId, out _);
        _questionAnswerSources.TryRemove(sessionId, out _);
    }

    public async Task CleanupAllAsync()
    {
        foreach (var cts in _sessionCts.Values)
        {
            try
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        _sessionCts.Clear();
        _sessionRunIds.Clear();
        _currentMessageTurnIds.Clear();
        _agentSessionIds.Clear();
        _sessionToolUses.Clear();
        _questionAnswerSources.Clear();
    }
}
