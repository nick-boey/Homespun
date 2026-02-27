using Fleece.Core.Models;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating an issue.
/// </summary>
public class CreateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType Type { get; set; } = IssueType.Task;

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Execution mode for child issues (Series or Parallel).
    /// </summary>
    public ExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Optional working branch ID. If not provided, the client can trigger AI generation on the edit page.
    /// </summary>
    public string? WorkingBranchId { get; set; }

    /// <summary>
    /// Optional parent issue ID. If provided, the new issue will have this issue as its parent.
    /// </summary>
    public string? ParentIssueId { get; set; }

    /// <summary>
    /// Optional sort order for the parent relationship. Used when creating siblings in series-mode parents.
    /// </summary>
    public string? ParentSortOrder { get; set; }

    /// <summary>
    /// Optional child issue ID. If provided, the new issue will become the parent of this issue.
    /// </summary>
    public string? ChildIssueId { get; set; }
}

/// <summary>
/// Request model for updating an issue.
/// </summary>
public class UpdateIssueRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Issue title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Issue status.
    /// </summary>
    public IssueStatus? Status { get; set; }

    /// <summary>
    /// Issue type.
    /// </summary>
    public IssueType? Type { get; set; }

    /// <summary>
    /// Issue description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Issue priority (1-5).
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Optional working branch ID update.
    /// </summary>
    public string? WorkingBranchId { get; set; }
}

/// <summary>
/// Response model for resolved branch lookup.
/// </summary>
public class ResolvedBranchResponse
{
    /// <summary>
    /// The resolved branch name, or null if no existing branch was found.
    /// </summary>
    public string? BranchName { get; set; }
}

/// <summary>
/// Request model for setting an issue's parent.
/// </summary>
public class SetParentRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The ID of the issue that will become the parent.
    /// </summary>
    public required string ParentIssueId { get; set; }

    /// <summary>
    /// If true, adds the parent to existing parents. If false (default), replaces all existing parents.
    /// </summary>
    public bool AddToExisting { get; set; } = false;
}
