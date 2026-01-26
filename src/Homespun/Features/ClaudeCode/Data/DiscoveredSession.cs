namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents a Claude session discovered from Claude Code's native storage.
/// Sessions are stored at ~/.claude/projects/[encoded-path]/[session-uuid].jsonl
/// </summary>
/// <param name="SessionId">The session UUID from the filename (used with --resume)</param>
/// <param name="FilePath">Full path to the .jsonl file</param>
/// <param name="LastModified">File last write time</param>
/// <param name="FileSize">File size in bytes</param>
public record DiscoveredSession(
    string SessionId,
    string FilePath,
    DateTime LastModified,
    long FileSize
);
