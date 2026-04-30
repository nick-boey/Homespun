using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Notifications;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.PullRequests;
using Microsoft.AspNetCore.SignalR;

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
    ILogger<PRStatusResolver> logger,
    IHubContext<NotificationHub>? notificationHub = null,
    IServiceProvider? services = null) : IPRStatusResolver
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

        // Load cache from disk before processing to ensure we have the latest data
        graphCacheService.LoadCacheForProject(projectId, project.LocalPath);

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
                    // Try to move from open to closed list
                    var moved = await graphCacheService.UpdatePRStatusAsync(
                        projectId,
                        project.LocalPath,
                        removedPr.GitHubPrNumber.Value,
                        PullRequestStatus.Merged,
                        mergedAt: prInfo.MergedAt,
                        closedAt: null,
                        issueId: removedPr.FleeceIssueId);

                    // If not in open list, add directly to closed list
                    if (!moved)
                    {
                        await graphCacheService.AddClosedPRAsync(
                            projectId,
                            project.LocalPath,
                            prInfo,
                            removedPr.FleeceIssueId);
                    }

                    // Update linked Fleece issue to Complete
                    if (!string.IsNullOrEmpty(removedPr.FleeceIssueId))
                    {
                        await issuePrLinkingService.UpdateIssueStatusFromPRAsync(
                            projectId, removedPr.FleeceIssueId, PullRequestStatus.Merged, removedPr.GitHubPrNumber.Value);
                    }

                    await InvalidateGraphSnapshotAsync(projectId, removedPr.FleeceIssueId);

                    logger.LogInformation(
                        "PR #{PrNumber} resolved as Merged (merged at {MergedAt})",
                        removedPr.GitHubPrNumber.Value, prInfo.MergedAt);
                }
                else if (prInfo.Status == PullRequestStatus.Closed)
                {
                    // Try to move from open to closed list
                    var moved = await graphCacheService.UpdatePRStatusAsync(
                        projectId,
                        project.LocalPath,
                        removedPr.GitHubPrNumber.Value,
                        PullRequestStatus.Closed,
                        mergedAt: null,
                        closedAt: prInfo.ClosedAt,
                        issueId: removedPr.FleeceIssueId);

                    // If not in open list, add directly to closed list
                    if (!moved)
                    {
                        await graphCacheService.AddClosedPRAsync(
                            projectId,
                            project.LocalPath,
                            prInfo,
                            removedPr.FleeceIssueId);
                    }

                    // Update linked Fleece issue to Closed
                    if (!string.IsNullOrEmpty(removedPr.FleeceIssueId))
                    {
                        await issuePrLinkingService.UpdateIssueStatusFromPRAsync(
                            projectId, removedPr.FleeceIssueId, PullRequestStatus.Closed, removedPr.GitHubPrNumber.Value);
                    }

                    await InvalidateGraphSnapshotAsync(projectId, removedPr.FleeceIssueId);

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

    /// <summary>
    /// A PR transitioning to Merged or Closed flips the linked Fleece issue's
    /// status (Complete / Closed) and may evict the local clone for that branch
    /// — both of which reshape <c>TaskGraphResponse.OpenSpecStates</c> for the
    /// issue. Invalidate via the hub helper so connected clients refetch within
    /// ~1s instead of waiting up to 10s for the refresher tick. Hub + services
    /// are nullable so unit tests can construct the resolver without wiring
    /// SignalR; the snapshot store stays correct on its own via the existing
    /// graph-cache writes when the broadcast is omitted.
    /// </summary>
    private async Task InvalidateGraphSnapshotAsync(string projectId, string? fleeceIssueId)
    {
        if (notificationHub is null || services is null)
        {
            return;
        }

        await notificationHub.BroadcastIssueTopologyChanged(
            services,
            projectId,
            IssueChangeType.Updated,
            string.IsNullOrEmpty(fleeceIssueId) ? null : fleeceIssueId);
    }
}
