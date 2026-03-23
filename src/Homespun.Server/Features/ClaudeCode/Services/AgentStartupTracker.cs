using System.Collections.Concurrent;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Thread-safe implementation of agent startup tracking.
/// </summary>
public class AgentStartupTracker(ILogger<AgentStartupTracker> logger) : IAgentStartupTracker
{
    private readonly ConcurrentDictionary<string, AgentStartupState> _states = new();
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(6);

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
        logger.LogDebug("Marked entity {EntityId} as Starting", entityId);
        OnStateChanged?.Invoke(entityId, state);
    }

    /// <inheritdoc />
    public bool TryMarkAsStarting(string entityId)
    {
        var newState = new AgentStartupState
        {
            EntityId = entityId,
            Status = AgentStartupStatus.Starting
        };

        // Try to add first (fast path for new entries)
        if (_states.TryAdd(entityId, newState))
        {
            logger.LogDebug("Marked entity {EntityId} as Starting (new entry)", entityId);
            OnStateChanged?.Invoke(entityId, newState);
            return true;
        }

        // Entry exists - check if we can override it
        if (_states.TryGetValue(entityId, out var existing))
        {
            // Terminal states (Started/Failed) can be overridden - the previous startup completed
            if (existing.Status is AgentStartupStatus.Started or AgentStartupStatus.Failed)
            {
                _states[entityId] = newState;
                logger.LogDebug(
                    "Marked entity {EntityId} as Starting (overriding terminal state {PreviousStatus})",
                    entityId, existing.Status);
                OnStateChanged?.Invoke(entityId, newState);
                return true;
            }

            // Starting state - check if stale (older than 6 minutes, above the 5-min timeout)
            var age = DateTime.UtcNow - existing.StartedAt;
            if (age > StaleThreshold)
            {
                _states[entityId] = newState;
                logger.LogWarning(
                    "Overriding stale Starting entry for entity {EntityId} (age: {Age})",
                    entityId, age);
                OnStateChanged?.Invoke(entityId, newState);
                return true;
            }

            // Active Starting state - block
            logger.LogDebug(
                "TryMarkAsStarting blocked for entity {EntityId} (existing state: {Status}, age: {Age})",
                entityId, existing.Status, age);
            return false;
        }

        // Race condition: entry was removed between TryAdd and TryGetValue, retry
        if (_states.TryAdd(entityId, newState))
        {
            logger.LogDebug("Marked entity {EntityId} as Starting (retry after race)", entityId);
            OnStateChanged?.Invoke(entityId, newState);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void MarkAsStarted(string entityId)
    {
        if (_states.TryGetValue(entityId, out var state))
        {
            state.Status = AgentStartupStatus.Started;
            logger.LogDebug("Marked entity {EntityId} as Started", entityId);
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
            logger.LogDebug("Marked entity {EntityId} as Failed: {Error}", entityId, error);
            OnStateChanged?.Invoke(entityId, state);
        }
    }

    /// <inheritdoc />
    public void Clear(string entityId)
    {
        _states.TryRemove(entityId, out _);
        logger.LogDebug("Cleared startup state for entity {EntityId}", entityId);
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
