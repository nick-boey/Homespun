using Homespun.Features.Git;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for resolving the branch name for an issue by checking linked PRs, working branch IDs, and existing clones.
/// </summary>
public class IssueBranchResolverService(
    IDataStore dataStore,
    IGitCloneService gitCloneService,
    IFleeceService fleeceService,
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

        if (string.IsNullOrEmpty(project.LocalPath))
        {
            logger.LogWarning("Project {ProjectId} has no local path configured", projectId);
            return null;
        }

        // 2. Check issue's WorkingBranchId
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue != null && !string.IsNullOrWhiteSpace(issue.WorkingBranchId))
        {
            var branchName = BranchNameGenerator.GenerateBranchNamePreview(
                issueId, issue.Type, issue.Title, issue.WorkingBranchId);
            logger.LogDebug("Resolved branch {BranchName} from WorkingBranchId for issue {IssueId}",
                branchName, issueId);
            return branchName;
        }

        // 3. Check for existing clones with matching issue ID in branch name
        var clones = await gitCloneService.ListClonesAsync(project.LocalPath);

        foreach (var clone in clones)
        {
            if (string.IsNullOrEmpty(clone.Branch))
            {
                continue;
            }

            // Extract the branch name without refs/heads/ prefix
            var cloneBranchName = clone.Branch.Replace("refs/heads/", "");

            // Use BranchNameParser to extract issue ID from the branch name
            var extractedIssueId = BranchNameParser.ExtractIssueId(cloneBranchName);

            if (extractedIssueId == issueId)
            {
                logger.LogDebug("Found existing clone with branch {BranchName} for issue {IssueId}",
                    cloneBranchName, issueId);
                return cloneBranchName;
            }
        }

        logger.LogDebug("No linked PR, working branch ID, or existing clone found for issue {IssueId}", issueId);
        return null;
    }
}
