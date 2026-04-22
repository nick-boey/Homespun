using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.Notifications;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Side-effectful wrapper around <see cref="IChangeScannerService"/>:
/// auto-links single orphans and auto-completes Fleece issues when their change has archived.
///
/// Both side-effects (sidecar write + archive-triggered Fleece transition)
/// invalidate any warm task-graph snapshot for the project and broadcast
/// <c>IssuesChanged</c> via <see cref="NotificationHubExtensions.BroadcastIssueTopologyChanged"/>
/// so connected clients refetch without waiting for the refresher tick.
/// </summary>
public class ChangeReconciliationService(
    IChangeScannerService scanner,
    IFleeceIssueTransitionService transitionService,
    ILogger<ChangeReconciliationService> logger,
    IProjectTaskGraphSnapshotStore? snapshotStore = null,
    IHubContext<NotificationHub>? notificationHub = null,
    IServiceProvider? services = null) : IChangeReconciliationService
{
    /// <inheritdoc />
    public async Task<BranchScanResult> ReconcileAsync(
        string projectId,
        string clonePath,
        string branchFleeceId,
        string? baseBranch = null,
        CancellationToken ct = default)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.reconcile");
        activity?.SetTag("project.id", projectId);

        var scan = await scanner.ScanBranchAsync(clonePath, branchFleeceId, baseBranch, ct);

        // 1. Auto-write sidecar for single-orphan case so the next scan picks it up as linked.
        var linkedOrphan = await scanner.TryAutoLinkSingleOrphanAsync(scan, branchFleeceId, ct);
        if (linkedOrphan is not null)
        {
            scan = await scanner.ScanBranchAsync(clonePath, branchFleeceId, baseBranch, ct);
            await InvalidateAndBroadcastAsync(projectId);
        }

        // 2. Auto-transition fleece issue to complete when any linked change is archived.
        if (scan.LinkedChanges.Any(c => c.IsArchived))
        {
            var currentStatus = await transitionService.GetStatusAsync(projectId, branchFleeceId);
            if (currentStatus is not null
                && currentStatus != IssueStatus.Complete
                && currentStatus != IssueStatus.Closed)
            {
                var result = await transitionService.TransitionToCompleteAsync(projectId, branchFleeceId);
                if (!result.Success)
                {
                    logger.LogWarning(
                        "Failed to auto-complete issue {Issue} after archived change detected: {Error}",
                        branchFleeceId, result.Error);
                }
                else
                {
                    await InvalidateAndBroadcastAsync(projectId);
                }
            }
        }

        return scan;
    }

    private async Task InvalidateAndBroadcastAsync(string projectId)
    {
        if (notificationHub is not null && services is not null)
        {
            await notificationHub.BroadcastIssueTopologyChanged(
                services, projectId, IssueChangeType.Updated, issueId: null);
        }
        else
        {
            // Fallback for tests that don't wire the hub — preserve the pre-existing invalidation.
            snapshotStore?.InvalidateProject(projectId);
        }
    }
}
