using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for discovering Claude sessions from Claude Code's native storage.
/// Sessions are stored at ~/.claude/projects/[encoded-path]/[session-uuid].jsonl
/// </summary>
public interface IClaudeSessionDiscovery
{
    /// <summary>
    /// Discovers all Claude sessions for a given working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory to find sessions for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered sessions ordered by last modified (newest first)</returns>
    Task<IReadOnlyList<DiscoveredSession>> DiscoverSessionsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a session exists in Claude's storage.
    /// </summary>
    /// <param name="sessionId">The session UUID</param>
    /// <param name="workingDirectory">The working directory</param>
    /// <returns>True if the session file exists</returns>
    bool SessionExists(string sessionId, string workingDirectory);

    /// <summary>
    /// Gets the file path for a session.
    /// </summary>
    /// <param name="sessionId">The session UUID</param>
    /// <param name="workingDirectory">The working directory</param>
    /// <returns>Full path to the session file, or null if not found</returns>
    string? GetSessionFilePath(string sessionId, string workingDirectory);

    /// <summary>
    /// Counts the number of lines (approximate message count) in a session file.
    /// </summary>
    /// <param name="sessionId">The session UUID</param>
    /// <param name="workingDirectory">The working directory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of lines in the JSONL file, or null if file not found</returns>
    Task<int?> GetMessageCountAsync(
        string sessionId,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
