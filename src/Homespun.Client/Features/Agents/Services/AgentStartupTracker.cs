using System.Collections.Concurrent;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Client.Services;

/// <summary>
/// Client-side agent startup tracker for UI feedback.
/// </summary>
public interface IAgentStartupTracker
{
    event Action<string, AgentStartupState>? OnStateChanged;
    void MarkAsStarting(string entityId);
    bool TryMarkAsStarting(string entityId);
    void MarkAsStarted(string entityId);
    void MarkAsFailed(string entityId, string error);
    void Clear(string entityId);
    bool IsStarting(string entityId);
    AgentStartupState? GetState(string entityId);
}

/// <summary>
/// In-memory client-side implementation of agent startup tracking.
/// </summary>
public class AgentStartupTracker : IAgentStartupTracker
{
    private readonly ConcurrentDictionary<string, AgentStartupState> _states = new();

    public event Action<string, AgentStartupState>? OnStateChanged;

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

    public bool TryMarkAsStarting(string entityId)
    {
        return _states.TryAdd(entityId, new AgentStartupState
        {
            EntityId = entityId,
            Status = AgentStartupStatus.Starting
        });
    }

    public void MarkAsStarted(string entityId)
    {
        if (_states.TryGetValue(entityId, out var state))
        {
            state.Status = AgentStartupStatus.Started;
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    public void MarkAsFailed(string entityId, string error)
    {
        if (_states.TryGetValue(entityId, out var state))
        {
            state.Status = AgentStartupStatus.Failed;
            state.ErrorMessage = error;
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    public void Clear(string entityId)
    {
        if (_states.TryRemove(entityId, out var state))
        {
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    public bool IsStarting(string entityId)
    {
        return _states.TryGetValue(entityId, out var state) && state.Status == AgentStartupStatus.Starting;
    }

    public AgentStartupState? GetState(string entityId)
    {
        return _states.TryGetValue(entityId, out var state) ? state : null;
    }
}
