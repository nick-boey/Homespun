namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for resolving the branch name for an issue by checking linked PRs and existing clones.
/// </summary>
public interface IIssueBranchResolverService
{
    /// <summary>
    /// Resolves the branch name for an issue by checking:
    /// 1. Linked PRs that have the issue ID set in BeadsIssueId
    /// 2. Existing clones whose branch name contains the issue ID
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The issue ID to resolve the branch for</param>
    /// <returns>The resolved branch name, or null if no matching PR or clone found</returns>
    Task<string?> ResolveIssueBranchAsync(string projectId, string issueId);
}
