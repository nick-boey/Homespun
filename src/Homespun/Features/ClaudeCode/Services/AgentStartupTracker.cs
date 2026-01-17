using System.Collections.Concurrent;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Thread-safe implementation of agent startup tracking.
/// </summary>
public class AgentStartupTracker : IAgentStartupTracker
{
    private readonly ConcurrentDictionary<string, AgentStartupState> _states = new();

    /// <inheritdoc />
    public event Action<string, AgentStartupState>? OnStateChanged;

    /// <inheritdoc />
    public void MarkAsStarting(string entityId)
    {
        var state = new AgentStartupState
        {
            EntityId = entityId,
            Status = AgentStartupStatus.Starting
        };
        _states[entityId] = state;
        OnStateChanged?.Invoke(entityId, state);
    }

    /// <inheritdoc />
    public void MarkAsStarted(string entityId)
    {
        if (_states.TryGetValue(entityId, out var state))
        {
            state.Status = AgentStartupStatus.Started;
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    /// <inheritdoc />
    public void MarkAsFailed(string entityId, string error)
    {
        if (_states.TryGetValue(entityId, out var state))
        {
            state.Status = AgentStartupStatus.Failed;
            state.ErrorMessage = error;
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    /// <inheritdoc />
    public void Clear(string entityId)
    {
        _states.TryRemove(entityId, out _);
    }

    /// <inheritdoc />
    public bool IsStarting(string entityId)
    {
        return _states.TryGetValue(entityId, out var state) && state.Status == AgentStartupStatus.Starting;
    }

    /// <inheritdoc />
    public AgentStartupState? GetState(string entityId)
    {
        return _states.TryGetValue(entityId, out var state) ? state : null;
    }
}
