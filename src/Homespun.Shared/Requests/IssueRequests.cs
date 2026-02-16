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
