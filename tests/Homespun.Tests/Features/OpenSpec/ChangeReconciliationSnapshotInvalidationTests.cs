using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Snapshots;
using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

/// <summary>
/// Tier 5 scenario: "Explicit invalidation triggers immediate refresh" —
/// sidecar auto-link + archive auto-transition must bust the task-graph
/// snapshot for the project.
/// </summary>
[TestFixture]
public class ChangeReconciliationSnapshotInvalidationTests
{
    private const string ProjectId = "proj-42";
    private const string FleeceId = "issue-7";
    private const string ClonePath = "/tmp/clone";

    [Test]
    public async Task AutoLink_Single_Orphan_Invalidates_Snapshot()
    {
        var scanner = new Mock<IChangeScannerService>();
        var store = new Mock<IProjectTaskGraphSnapshotStore>();

        var initial = new BranchScanResult { BranchFleeceId = FleeceId };
        var postLink = new BranchScanResult { BranchFleeceId = FleeceId };

        scanner
            .SetupSequence(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initial)
            .ReturnsAsync(postLink);
        scanner
            .Setup(s => s.TryAutoLinkSingleOrphanAsync(initial, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("my-change");

        var service = new ChangeReconciliationService(
            scanner.Object,
            new Mock<IFleeceIssueTransitionService>().Object,
            NullLogger<ChangeReconciliationService>.Instance,
            store.Object);

        await service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        store.Verify(s => s.InvalidateProject(ProjectId), Times.Once);
    }

    [Test]
    public async Task Archive_Auto_Transition_Invalidates_Snapshot()
    {
        var scanner = new Mock<IChangeScannerService>();
        var transition = new Mock<IFleeceIssueTransitionService>();
        var store = new Mock<IProjectTaskGraphSnapshotStore>();

        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = true }
            }
        };

        scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        transition.Setup(t => t.GetStatusAsync(ProjectId, FleeceId))
            .ReturnsAsync(IssueStatus.Progress);
        transition.Setup(t => t.TransitionToCompleteAsync(ProjectId, FleeceId, It.IsAny<int?>()))
            .ReturnsAsync(FleeceTransitionResult.Ok(IssueStatus.Progress, IssueStatus.Complete));

        var service = new ChangeReconciliationService(
            scanner.Object,
            transition.Object,
            NullLogger<ChangeReconciliationService>.Instance,
            store.Object);

        await service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        store.Verify(s => s.InvalidateProject(ProjectId), Times.Once);
    }

    [Test]
    public async Task No_Side_Effects_Does_Not_Invalidate_Snapshot()
    {
        var scanner = new Mock<IChangeScannerService>();
        var store = new Mock<IProjectTaskGraphSnapshotStore>();

        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = false }
            }
        };

        scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = new ChangeReconciliationService(
            scanner.Object,
            new Mock<IFleeceIssueTransitionService>().Object,
            NullLogger<ChangeReconciliationService>.Instance,
            store.Object);

        await service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        store.Verify(s => s.InvalidateProject(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Failed_Transition_Does_Not_Invalidate_Snapshot()
    {
        var scanner = new Mock<IChangeScannerService>();
        var transition = new Mock<IFleeceIssueTransitionService>();
        var store = new Mock<IProjectTaskGraphSnapshotStore>();

        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = true }
            }
        };

        scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        transition.Setup(t => t.GetStatusAsync(ProjectId, FleeceId))
            .ReturnsAsync(IssueStatus.Progress);
        transition.Setup(t => t.TransitionToCompleteAsync(ProjectId, FleeceId, It.IsAny<int?>()))
            .ReturnsAsync(FleeceTransitionResult.Fail("no such issue"));

        var service = new ChangeReconciliationService(
            scanner.Object,
            transition.Object,
            NullLogger<ChangeReconciliationService>.Instance,
            store.Object);

        await service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        store.Verify(s => s.InvalidateProject(It.IsAny<string>()), Times.Never);
    }
}
