using System.Collections.Concurrent;
using Homespun.Features.ClaudeCode.Data;

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
        if (!_sessions.ContainsKey(session.Id))
        {
            return false;
        }

        _sessions[session.Id] = session;
        return true;
    }

    /// <inheritdoc />
    public bool Remove(string sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }
}
