using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.GitHub;

/// <summary>
/// Resolves the final status of PRs that were removed from local tracking.
/// When PRs transition from open to closed/merged, this service fetches their
/// actual status from GitHub and updates the graph cache.
/// </summary>
public class PRStatusResolver(
    IGitHubService gitHubService,
    IGraphCacheService graphCacheService,
    IIssuePrLinkingService issuePrLinkingService,
    IDataStore dataStore,
    ILogger<PRStatusResolver> logger) : IPRStatusResolver
{
    /// <inheritdoc />
    public async Task ResolveClosedPRStatusesAsync(string projectId, List<RemovedPrInfo> removedPrs)
    {
        if (removedPrs.Count == 0)
        {
            return;
        }

        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Cannot resolve PR statuses: project {ProjectId} not found", projectId);
            return;
        }

        logger.LogInformation(
            "Resolving status for {Count} removed PRs in project {ProjectId}",
            removedPrs.Count, projectId);

        foreach (var removedPr in removedPrs)
        {
            if (!removedPr.GitHubPrNumber.HasValue)
            {
                logger.LogDebug("Skipping PR {PullRequestId}: no GitHub PR number", removedPr.PullRequestId);
                continue;
            }

            try
            {
                var prInfo = await gitHubService.GetPullRequestAsync(projectId, removedPr.GitHubPrNumber.Value);
                if (prInfo == null)
                {
                    logger.LogWarning(
                        "Could not fetch PR #{PrNumber} from GitHub for project {ProjectId}",
                        removedPr.GitHubPrNumber.Value, projectId);
                    continue;
                }

                // Determine final status
                if (prInfo.Status == PullRequestStatus.Merged)
                {
                    await graphCacheService.UpdatePRStatusAsync(
                        projectId,
                        project.LocalPath,
                        removedPr.GitHubPrNumber.Value,
                        PullRequestStatus.Merged,
                        mergedAt: prInfo.MergedAt,
                        closedAt: null,
                        issueId: removedPr.BeadsIssueId);

                    // Update linked Fleece issue to Complete
                    if (!string.IsNullOrEmpty(removedPr.BeadsIssueId))
                    {
                        await issuePrLinkingService.UpdateIssueStatusFromPRAsync(
                            projectId, removedPr.BeadsIssueId, PullRequestStatus.Merged, removedPr.GitHubPrNumber.Value);
                    }

                    logger.LogInformation(
                        "PR #{PrNumber} resolved as Merged (merged at {MergedAt})",
                        removedPr.GitHubPrNumber.Value, prInfo.MergedAt);
                }
                else if (prInfo.Status == PullRequestStatus.Closed)
                {
                    await graphCacheService.UpdatePRStatusAsync(
                        projectId,
                        project.LocalPath,
                        removedPr.GitHubPrNumber.Value,
                        PullRequestStatus.Closed,
                        mergedAt: null,
                        closedAt: prInfo.ClosedAt,
                        issueId: removedPr.BeadsIssueId);

                    // Update linked Fleece issue to Closed
                    if (!string.IsNullOrEmpty(removedPr.BeadsIssueId))
                    {
                        await issuePrLinkingService.UpdateIssueStatusFromPRAsync(
                            projectId, removedPr.BeadsIssueId, PullRequestStatus.Closed, removedPr.GitHubPrNumber.Value);
                    }

                    logger.LogInformation(
                        "PR #{PrNumber} resolved as Closed (closed at {ClosedAt})",
                        removedPr.GitHubPrNumber.Value, prInfo.ClosedAt);
                }
                else
                {
                    // PR might still be open or in an unexpected state
                    logger.LogWarning(
                        "PR #{PrNumber} has unexpected status {Status} after being removed from open PRs",
                        removedPr.GitHubPrNumber.Value, prInfo.Status);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error resolving status for PR #{PrNumber} in project {ProjectId}",
                    removedPr.GitHubPrNumber.Value, projectId);
                // Continue processing other PRs
            }
        }
    }
}
