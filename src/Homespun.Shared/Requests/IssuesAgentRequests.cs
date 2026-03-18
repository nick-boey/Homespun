using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Issues;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating an Issues Agent session.
/// </summary>
public class CreateIssuesAgentSessionRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The Claude model to use (e.g., "sonnet", "opus", "haiku").
    /// If not specified, defaults to project's default model.
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Response model for creating an Issues Agent session.
/// </summary>
public class CreateIssuesAgentSessionResponse
{
    /// <summary>
    /// The created session ID.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// The branch name created for the session.
    /// </summary>
    public required string BranchName { get; set; }

    /// <summary>
    /// The path to the clone where the agent is working.
    /// </summary>
    public required string ClonePath { get; set; }
}

/// <summary>
/// Response model for getting the issue diff for a session.
/// </summary>
public class IssueDiffResponse
{
    /// <summary>
    /// The task graph for the main branch.
    /// </summary>
    public required TaskGraphResponse MainBranchGraph { get; set; }

    /// <summary>
    /// The task graph for the session's branch (with changes).
    /// </summary>
    public required TaskGraphResponse SessionBranchGraph { get; set; }

    /// <summary>
    /// List of changes between main and session branches.
    /// </summary>
    public required List<IssueChangeDto> Changes { get; set; }

    /// <summary>
    /// Summary of change counts.
    /// </summary>
    public required IssueDiffSummary Summary { get; set; }
}

/// <summary>
/// Summary of changes between branches.
/// </summary>
public class IssueDiffSummary
{
    /// <summary>
    /// Number of issues created.
    /// </summary>
    public int Created { get; set; }

    /// <summary>
    /// Number of issues updated.
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Number of issues deleted.
    /// </summary>
    public int Deleted { get; set; }
}

/// <summary>
/// Request model for accepting issue changes from a session.
/// </summary>
public class AcceptIssuesAgentChangesRequest
{
    /// <summary>
    /// The session ID whose changes should be accepted.
    /// </summary>
    public required string SessionId { get; set; }
}

/// <summary>
/// Response model for accepting issue changes.
/// </summary>
public class AcceptIssuesAgentChangesResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// URL to redirect to after accepting changes.
    /// </summary>
    public string? RedirectUrl { get; set; }
}
