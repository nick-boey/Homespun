
namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Interface for managing Claude Code sessions in memory.
/// </summary>
public interface IClaudeSessionStore
{
    /// <summary>
    /// Adds a session to the store.
    /// </summary>
    void Add(ClaudeSession session);

    /// <summary>
    /// Gets a session by its ID.
    /// </summary>
    ClaudeSession? GetById(string sessionId);

    /// <summary>
    /// Gets a session by its associated entity ID.
    /// </summary>
    ClaudeSession? GetByEntityId(string entityId);

    /// <summary>
    /// Gets all sessions for a given entity ID (issue/PR).
    /// Unlike <see cref="GetByEntityId"/> which returns only the first match,
    /// this returns all sessions including superseded ones.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetAllByEntityId(string entityId);

    /// <summary>
    /// Gets all sessions for a project.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetByProjectId(string projectId);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetAll();

    /// <summary>
    /// Updates an existing session.
    /// </summary>
    bool Update(ClaudeSession session);

    /// <summary>
    /// Removes a session from the store.
    /// </summary>
    bool Remove(string sessionId);
}
