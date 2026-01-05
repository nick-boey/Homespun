using System.Diagnostics;

namespace Homespun.Features.OpenCode.Models;

/// <summary>
/// Represents a running OpenCode server instance associated with a PullRequest.
/// </summary>
public class OpenCodeServer
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string PullRequestId { get; init; }
    public required string WorktreePath { get; init; }
    public required int Port { get; init; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public Process? Process { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public OpenCodeServerStatus Status { get; set; } = OpenCodeServerStatus.Starting;
    public string? ActiveSessionId { get; set; }
    
    /// <summary>
    /// Whether the server was started with --continue to resume an existing session.
    /// </summary>
    public bool ContinueSession { get; init; }
}

public enum OpenCodeServerStatus
{
    Starting,
    Running,
    Stopped,
    Failed
}
