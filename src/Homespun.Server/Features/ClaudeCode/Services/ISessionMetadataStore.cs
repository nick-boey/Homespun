
namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Store for session metadata that maps Claude sessions to our entities (PR/issue).
/// This is lightweight data stored separately from Claude's session files.
/// </summary>
public interface ISessionMetadataStore
{
    /// <summary>
    /// Gets metadata for a specific session.
    /// </summary>
    /// <param name="sessionId">Claude's session UUID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Session metadata if found, null otherwise</returns>
    Task<SessionMetadata?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all metadata for sessions associated with an entity.
    /// </summary>
    /// <param name="entityId">Our PR or issue ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of session metadata for the entity</returns>
    Task<IReadOnlyList<SessionMetadata>> GetByEntityIdAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves session metadata (upsert).
    /// </summary>
    /// <param name="metadata">The metadata to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveAsync(SessionMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes metadata for a session.
    /// </summary>
    /// <param name="sessionId">Claude's session UUID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>All session metadata</returns>
    Task<IReadOnlyList<SessionMetadata>> GetAllAsync(CancellationToken cancellationToken = default);
}
