using Homespun.Features.Git;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for resolving the branch name for an issue by checking linked PRs and existing clones.
/// </summary>
public class IssueBranchResolverService(
    IDataStore dataStore,
    IGitCloneService gitCloneService,
    ILogger<IssueBranchResolverService> logger) : IIssueBranchResolverService
{
    /// <inheritdoc />
    public async Task<string?> ResolveIssueBranchAsync(string projectId, string issueId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when resolving branch for issue {IssueId}",
                projectId, issueId);
            return null;
        }

        // 1. Check for linked PR with matching BeadsIssueId
        var linkedPr = dataStore.GetPullRequestsByProject(projectId)
            .FirstOrDefault(pr => pr.BeadsIssueId == issueId && !string.IsNullOrEmpty(pr.BranchName));

        if (linkedPr != null)
        {
            logger.LogDebug("Found linked PR {PrId} with branch {BranchName} for issue {IssueId}",
                linkedPr.Id, linkedPr.BranchName, issueId);
            return linkedPr.BranchName;
        }

        // 2. Check for existing clones with matching issue ID in branch name
        if (string.IsNullOrEmpty(project.LocalPath))
        {
            logger.LogWarning("Project {ProjectId} has no local path configured", projectId);
            return null;
        }

        var clones = await gitCloneService.ListClonesAsync(project.LocalPath);

        foreach (var clone in clones)
        {
            if (string.IsNullOrEmpty(clone.Branch))
            {
                continue;
            }

            // Extract the branch name without refs/heads/ prefix
            var branchName = clone.Branch.Replace("refs/heads/", "");

            // Use BranchNameParser to extract issue ID from the branch name
            var extractedIssueId = BranchNameParser.ExtractIssueId(branchName);

            if (extractedIssueId == issueId)
            {
                logger.LogDebug("Found existing clone with branch {BranchName} for issue {IssueId}",
                    branchName, issueId);
                return branchName;
            }
        }

        logger.LogDebug("No linked PR or existing clone found for issue {IssueId}", issueId);
        return null;
    }
}
