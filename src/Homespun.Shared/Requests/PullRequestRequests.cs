using Homespun.Shared.Models.PullRequests;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a pull request.
/// </summary>
public class CreatePullRequestRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Pull request title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Pull request description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Git branch name.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Parent pull request ID for stacking.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Initial status.
    /// </summary>
    public OpenPullRequestStatus? Status { get; set; }
}

/// <summary>
/// Request model for updating a pull request.
/// </summary>
public class UpdatePullRequestRequest
{
    /// <summary>
    /// Pull request title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Pull request description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Git branch name.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// Parent pull request ID for stacking.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Status.
    /// </summary>
    public OpenPullRequestStatus? Status { get; set; }
}
