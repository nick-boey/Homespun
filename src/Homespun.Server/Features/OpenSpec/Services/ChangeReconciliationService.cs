using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Side-effectful wrapper around <see cref="IChangeScannerService"/>:
/// auto-links single orphans and auto-completes Fleece issues when their change has archived.
///
/// Both side-effects (sidecar write + archive-triggered Fleece transition)
/// invalidate any warm task-graph snapshot for the project so the next
/// `/taskgraph/data` call recomputes without waiting for the refresher tick.
/// </summary>
public class ChangeReconciliationService(
    IChangeScannerService scanner,
    IFleeceIssueTransitionService transitionService,
    ILogger<ChangeReconciliationService> logger,
    IProjectTaskGraphSnapshotStore? snapshotStore = null) : IChangeReconciliationService
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
            snapshotStore?.InvalidateProject(projectId);
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
                    snapshotStore?.InvalidateProject(projectId);
                }
            }
        }

        return scan;
    }
}
