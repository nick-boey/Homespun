using System.Collections.Concurrent;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Thread-safe in-memory store for Claude Code sessions.
/// </summary>
public class ClaudeSessionStore : IClaudeSessionStore
{
    private readonly ConcurrentDictionary<string, ClaudeSession> _sessions = new();

    /// <inheritdoc />
    public void Add(ClaudeSession session)
    {
        _sessions[session.Id] = session;
    }

    /// <inheritdoc />
    public ClaudeSession? GetById(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <inheritdoc />
    public ClaudeSession? GetByEntityId(string entityId)
    {
        return _sessions.Values.FirstOrDefault(s => s.EntityId == entityId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetAllByEntityId(string entityId)
    {
        return _sessions.Values
            .Where(s => s.EntityId == entityId)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetByProjectId(string projectId)
    {
        return _sessions.Values
            .Where(s => s.ProjectId == projectId)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetAll()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public bool Update(ClaudeSession session)
    {
        // Atomic compare-and-swap: only mutate the slot if it currently holds
        // a session with the same id. The pre-fix `ContainsKey` + indexer
        // pattern could race with a concurrent `Remove` and re-insert a
        // just-removed session — see FI-5 in
        // close-out-claude-agent-sessions-migration-gaps.
        while (_sessions.TryGetValue(session.Id, out var current))
        {
            if (_sessions.TryUpdate(session.Id, session, current))
            {
                return true;
            }
            // Another writer raced us to TryUpdate; retry against the
            // newly-observed value. Loop terminates because TryGetValue
            // returns false once a Remove has won.
        }

        return false;
    }

    /// <inheritdoc />
    public bool Remove(string sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }
}
