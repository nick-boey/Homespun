using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// Agent status data for JSON serialization.
/// </summary>
public class AgentStatusData
{
    /// <summary>
    /// Whether the agent is currently running/active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The session status.
    /// </summary>
    public required ClaudeSessionStatus Status { get; set; }

    /// <summary>
    /// The session ID for reference.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Project the session belongs to. Optional — included on the
    /// <c>/agent-statuses</c> endpoint response so clients can correlate
    /// without fetching the full session.
    /// </summary>
    public string? ProjectId { get; set; }

    public static AgentStatusData FromSession(ClaudeSession session)
        => new()
        {
            IsActive = session.Status.IsActive(),
            Status = session.Status,
            SessionId = session.Id,
            ProjectId = session.ProjectId
        };
}
