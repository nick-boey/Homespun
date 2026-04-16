using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.OpenSpec.Services;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.OpenSpec;

[TestFixture]
public class ChangeReconciliationServiceTests
{
    private Mock<IChangeScannerService> _scanner = null!;
    private Mock<IFleeceIssueTransitionService> _transition = null!;
    private ChangeReconciliationService _service = null!;

    private const string ProjectId = "proj-1";
    private const string FleeceId = "issue-1";
    private const string ClonePath = "/tmp/clone";

    [SetUp]
    public void SetUp()
    {
        _scanner = new Mock<IChangeScannerService>();
        _transition = new Mock<IFleeceIssueTransitionService>();
        _service = new ChangeReconciliationService(
            _scanner.Object,
            _transition.Object,
            NullLogger<ChangeReconciliationService>.Instance);
    }

    [Test]
    public async Task ReconcileAsync_ArchivedLinkedChange_TransitionsIssueToComplete()
    {
        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = true }
            }
        };

        _scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        _scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _transition.Setup(t => t.GetStatusAsync(ProjectId, FleeceId)).ReturnsAsync(IssueStatus.Progress);
        _transition.Setup(t => t.TransitionToCompleteAsync(ProjectId, FleeceId, It.IsAny<int?>()))
            .ReturnsAsync(FleeceTransitionResult.Ok(IssueStatus.Progress, IssueStatus.Complete));

        var result = await _service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        Assert.That(result, Is.SameAs(scan));
        _transition.Verify(t => t.TransitionToCompleteAsync(ProjectId, FleeceId, null), Times.Once);
    }

    [Test]
    public async Task ReconcileAsync_IssueAlreadyComplete_SkipsTransition()
    {
        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = true }
            }
        };

        _scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        _scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _transition.Setup(t => t.GetStatusAsync(ProjectId, FleeceId)).ReturnsAsync(IssueStatus.Complete);

        await _service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        _transition.Verify(t => t.TransitionToCompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Test]
    public async Task ReconcileAsync_LiveChangeOnly_DoesNotTransition()
    {
        var scan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "x", Directory = "/tmp/x", CreatedBy = "agent", IsArchived = false }
            }
        };

        _scanner.Setup(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scan);
        _scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(scan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await _service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        _transition.Verify(t => t.TransitionToCompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Test]
    public async Task ReconcileAsync_SingleOrphan_AutoLinksAndRescans()
    {
        var firstScan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            OrphanChanges = new List<OrphanChangeInfo>
            {
                new() { Name = "orphan", Directory = "/tmp/orphan" }
            }
        };

        var secondScan = new BranchScanResult
        {
            BranchFleeceId = FleeceId,
            LinkedChanges = new List<LinkedChangeInfo>
            {
                new() { Name = "orphan", Directory = "/tmp/orphan", CreatedBy = "agent", IsArchived = false }
            }
        };

        _scanner.SetupSequence(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstScan)
            .ReturnsAsync(secondScan);
        _scanner.Setup(s => s.TryAutoLinkSingleOrphanAsync(firstScan, FleeceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("orphan");

        var result = await _service.ReconcileAsync(ProjectId, ClonePath, FleeceId);

        Assert.That(result, Is.SameAs(secondScan));
        Assert.That(result.LinkedChanges, Has.Count.EqualTo(1));
        _scanner.Verify(s => s.ScanBranchAsync(ClonePath, FleeceId, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
