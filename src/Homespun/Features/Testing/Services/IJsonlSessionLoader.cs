using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Service for loading Claude Code sessions from JSONL files.
/// </summary>
public interface IJsonlSessionLoader
{
    /// <summary>
    /// Loads messages from a JSONL file.
    /// </summary>
    /// <param name="jsonlPath">Path to the JSONL file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of messages parsed from the file</returns>
    Task<List<ClaudeMessage>> LoadMessagesAsync(string jsonlPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a session from a directory containing JSONL and optional meta files.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing session files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A ClaudeSession with loaded messages, or null if no JSONL file found</returns>
    Task<ClaudeSession?> LoadSessionFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all sessions from a base directory containing project subdirectories.
    /// </summary>
    /// <param name="baseDirectory">Base directory containing project subdirectories with session files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all loaded sessions</returns>
    Task<List<ClaudeSession>> LoadAllSessionsAsync(string baseDirectory, CancellationToken cancellationToken = default);
}
