using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Discovers Claude sessions from Claude Code's native storage.
/// Sessions are stored at ~/.claude/projects/[encoded-path]/[session-uuid].jsonl
/// </summary>
public class ClaudeSessionDiscovery : IClaudeSessionDiscovery
{
    private readonly string _claudeProjectsDir;
    private readonly ILogger<ClaudeSessionDiscovery> _logger;

    public ClaudeSessionDiscovery(string claudeProjectsDir, ILogger<ClaudeSessionDiscovery> logger)
    {
        _claudeProjectsDir = claudeProjectsDir;
        _logger = logger;
    }

    /// <summary>
    /// Encodes a working directory path to Claude's storage format.
    /// Claude replaces path separators with hyphens.
    /// </summary>
    /// <param name="path">The working directory path</param>
    /// <returns>Encoded path for Claude's storage</returns>
    public static string EncodePath(string path)
    {
        return path.Replace("/", "-").Replace("\\", "-");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredSession>> DiscoverSessionsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var encodedPath = EncodePath(workingDirectory);
        var projectDir = Path.Combine(_claudeProjectsDir, encodedPath);

        if (!Directory.Exists(projectDir))
        {
            _logger.LogDebug("Project directory not found: {ProjectDir}", projectDir);
            return Task.FromResult<IReadOnlyList<DiscoveredSession>>([]);
        }

        try
        {
            var sessions = Directory.GetFiles(projectDir, "*.jsonl")
                .Select(filePath =>
                {
                    var fileInfo = new FileInfo(filePath);
                    return new DiscoveredSession(
                        SessionId: Path.GetFileNameWithoutExtension(filePath),
                        FilePath: filePath,
                        LastModified: fileInfo.LastWriteTimeUtc,
                        FileSize: fileInfo.Length
                    );
                })
                .OrderByDescending(s => s.LastModified)
                .ToList();

            _logger.LogDebug("Discovered {Count} sessions for {WorkingDirectory}", sessions.Count, workingDirectory);
            return Task.FromResult<IReadOnlyList<DiscoveredSession>>(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering sessions for {WorkingDirectory}", workingDirectory);
            return Task.FromResult<IReadOnlyList<DiscoveredSession>>([]);
        }
    }

    /// <inheritdoc />
    public bool SessionExists(string sessionId, string workingDirectory)
    {
        var filePath = GetSessionFilePath(sessionId, workingDirectory);
        return filePath != null;
    }

    /// <inheritdoc />
    public string? GetSessionFilePath(string sessionId, string workingDirectory)
    {
        var encodedPath = EncodePath(workingDirectory);
        var filePath = Path.Combine(_claudeProjectsDir, encodedPath, $"{sessionId}.jsonl");
        return File.Exists(filePath) ? filePath : null;
    }

    /// <inheritdoc />
    public async Task<int?> GetMessageCountAsync(
        string sessionId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetSessionFilePath(sessionId, workingDirectory);
        if (filePath == null)
        {
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            // Count non-empty lines (JSONL format has one JSON object per line)
            return content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading session file {FilePath}", filePath);
            return null;
        }
    }
}
