
namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Store for persisting session messages to JSONL files.
/// Enables message history review and session resumption.
/// </summary>
public interface IMessageCacheStore
{
    /// <summary>
    /// Appends a message to the session's cache.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="message">The message to append</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AppendMessageAsync(string sessionId, ClaudeMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached messages for a session.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of messages in order they were appended</returns>
    Task<IReadOnlyList<ClaudeMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session summary info (message count, timestamps).
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary if session exists, null otherwise</returns>
    Task<SessionCacheSummary?> GetSessionSummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all cached sessions for a project.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of session summaries</returns>
    Task<IReadOnlyList<SessionCacheSummary>> ListSessionsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the session IDs for an entity (issue/PR).
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="entityId">The entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of session IDs</returns>
    Task<IReadOnlyList<string>> GetSessionIdsForEntityAsync(string projectId, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a session cache exists.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cache exists</returns>
    Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes a session cache with metadata.
    /// Must be called before appending messages.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="entityId">The entity ID (issue/PR)</param>
    /// <param name="projectId">The project ID</param>
    /// <param name="mode">The session mode</param>
    /// <param name="model">The model used</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        SessionMode? mode,
        string? model,
        CancellationToken cancellationToken = default);
}
