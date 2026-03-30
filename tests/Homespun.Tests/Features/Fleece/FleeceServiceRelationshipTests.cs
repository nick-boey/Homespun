using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceServiceRelationshipTests
{
    private string _tempDir = null!;
    private Mock<ILogger<FleeceService>> _mockLogger = null!;
    private Mock<IIssueSerializationQueue> _mockQueue = null!;
    private Mock<IIssueHistoryService> _mockHistoryService = null!;
    private FleeceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<FleeceService>>();
        _mockQueue = new Mock<IIssueSerializationQueue>();
        _mockQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<IssueWriteOperation>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        _mockHistoryService = new Mock<IIssueHistoryService>();
        _mockHistoryService
            .Setup(h => h.RecordSnapshotAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Issue>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new FleeceService(_mockQueue.Object, _mockHistoryService.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region GetPriorSiblingAsync Tests

    [Test]
    public async Task GetPriorSiblingAsync_WithNoSiblings_ReturnsNull()
    {
        // Arrange - create a single issue with a parent (but no siblings)
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, child.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_WithPriorSiblings_ReturnsImmediatePrior()
    {
        // Arrange - create a parent with three children in series
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);
        var child3 = await _service.CreateIssueAsync(_tempDir, "Child 3", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child3.Id, parent.Id);

        // Act - get the prior sibling of child3
        var result = await _service.GetPriorSiblingAsync(_tempDir, child3.Id);

        // Assert - should return child2 (the immediate prior sibling)
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(child2.Id));
    }

    [Test]
    public async Task GetPriorSiblingAsync_WithOnlyLaterSiblings_ReturnsNull()
    {
        // Arrange - create a parent with two children, query the first one
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - get the prior sibling of child1 (which is first)
        var result = await _service.GetPriorSiblingAsync(_tempDir, child1.Id);

        // Assert - should return null because child1 has no prior sibling
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_IssueWithNoParent_ReturnsNull()
    {
        // Arrange - create an issue without a parent
        var orphan = await _service.CreateIssueAsync(_tempDir, "Orphan Issue", IssueType.Task);

        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, orphan.Id);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPriorSiblingAsync_NonExistentIssue_ReturnsNull()
    {
        // Act
        var result = await _service.GetPriorSiblingAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region GetChildrenAsync Tests

    [Test]
    public async Task GetChildrenAsync_WithNoChildren_ReturnsEmpty()
    {
        // Arrange - create a parent issue with no children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);

        // Act
        var result = await _service.GetChildrenAsync(_tempDir, parent.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChildrenAsync_WithChildren_ReturnsSortedList()
    {
        // Arrange - create a parent with three children added in a specific order
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2", IssueType.Task);
        var child3 = await _service.CreateIssueAsync(_tempDir, "Child 3", IssueType.Task);

        // DependencyService assigns ascending sort orders in insertion order
        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child3.Id, parent.Id);

        // Act
        var result = await _service.GetChildrenAsync(_tempDir, parent.Id);

        // Assert - should be sorted by sortOrder (insertion order)
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Id, Is.EqualTo(child1.Id));
        Assert.That(result[1].Id, Is.EqualTo(child2.Id));
        Assert.That(result[2].Id, Is.EqualTo(child3.Id));
    }

    [Test]
    public async Task GetChildrenAsync_NonExistentParent_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetChildrenAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region GetBlockingIssuesAsync Tests

    [Test]
    public async Task GetBlockingIssuesAsync_WithNoBlockers_IsBlockedFalse()
    {
        // Arrange - create an issue with no children and no parent (no siblings)
        var issue = await _service.CreateIssueAsync(_tempDir, "Standalone Issue", IssueType.Task);

        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, issue.Id);

        // Assert
        Assert.That(result.IsBlocked, Is.False);
        Assert.That(result.OpenChildren, Is.Empty);
        Assert.That(result.OpenPriorSiblings, Is.Empty);
    }

    [Test]
    public async Task GetBlockingIssuesAsync_WithOpenChildren_ReturnsOpenChildren()
    {
        // Arrange - create a parent with open children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Open)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Progress)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Set child2 to Progress status
        await _service.UpdateIssueAsync(_tempDir, child2.Id, status: IssueStatus.Progress);

        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, parent.Id);

        // Assert
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenChildren, Has.Count.EqualTo(2));
        Assert.That(result.OpenChildren.Select(c => c.Id), Contains.Item(child1.Id));
        Assert.That(result.OpenChildren.Select(c => c.Id), Contains.Item(child2.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_WithOpenPriorSiblings_ReturnsOpenPriorSiblings()
    {
        // Arrange - create a parent with siblings in series
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Open)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Open)", IssueType.Task);
        var child3 = await _service.CreateIssueAsync(_tempDir, "Child 3 (Query)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child3.Id, parent.Id);

        // Act - check blocking issues for child3
        var result = await _service.GetBlockingIssuesAsync(_tempDir, child3.Id);

        // Assert - child1 and child2 are open prior siblings
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenPriorSiblings, Has.Count.EqualTo(2));
        Assert.That(result.OpenPriorSiblings.Select(s => s.Id), Contains.Item(child1.Id));
        Assert.That(result.OpenPriorSiblings.Select(s => s.Id), Contains.Item(child2.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_WithCompletedSiblings_NotBlocked()
    {
        // Arrange - create a parent with siblings where prior siblings are complete
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Complete)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Query)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Complete child1
        await _service.UpdateIssueAsync(_tempDir, child1.Id, status: IssueStatus.Complete);

        // Act - check blocking issues for child2
        var result = await _service.GetBlockingIssuesAsync(_tempDir, child2.Id);

        // Assert - no blocking siblings because child1 is complete
        Assert.That(result.IsBlocked, Is.False);
        Assert.That(result.OpenPriorSiblings, Is.Empty);
    }

    [Test]
    public async Task GetBlockingIssuesAsync_WithCompletedChildren_NotBlockedByThem()
    {
        // Arrange - create a parent with completed children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Complete)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Closed)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Complete/close the children
        await _service.UpdateIssueAsync(_tempDir, child1.Id, status: IssueStatus.Complete);
        await _service.UpdateIssueAsync(_tempDir, child2.Id, status: IssueStatus.Closed);

        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, parent.Id);

        // Assert - no blocking children because they are complete/closed
        Assert.That(result.IsBlocked, Is.False);
        Assert.That(result.OpenChildren, Is.Empty);
    }

    [Test]
    public async Task GetBlockingIssuesAsync_WithReviewStatusSiblings_IsBlocked()
    {
        // Arrange - create siblings where prior sibling is in Review status
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Feature);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Review)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Query)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Set child1 to Review status
        await _service.UpdateIssueAsync(_tempDir, child1.Id, status: IssueStatus.Review);

        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, child2.Id);

        // Assert - should be blocked by child1 in Review
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenPriorSiblings, Has.Count.EqualTo(1));
        Assert.That(result.OpenPriorSiblings[0].Id, Is.EqualTo(child1.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_NonExistentIssue_ReturnsEmptyResult()
    {
        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result.IsBlocked, Is.False);
        Assert.That(result.OpenChildren, Is.Empty);
        Assert.That(result.OpenPriorSiblings, Is.Empty);
    }

    [Test]
    public async Task GetBlockingIssuesAsync_BothOpenChildrenAndSiblings_ReturnsAll()
    {
        // Arrange - create a structure with both blocking children and blocking prior siblings
        var grandparent = await _service.CreateIssueAsync(_tempDir, "Grandparent", IssueType.Feature);
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Task);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2 (Query)", IssueType.Task);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child of Parent2", IssueType.Task);

        // Set up hierarchy: grandparent -> [parent1, parent2], parent2 -> child1
        await _service.AddParentAsync(_tempDir, parent1.Id, grandparent.Id);
        await _service.AddParentAsync(_tempDir, parent2.Id, grandparent.Id);
        await _service.AddParentAsync(_tempDir, child1.Id, parent2.Id);

        // Act - check blocking issues for parent2
        var result = await _service.GetBlockingIssuesAsync(_tempDir, parent2.Id);

        // Assert - should be blocked by both open prior sibling (parent1) and open child (child1)
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenPriorSiblings, Has.Count.EqualTo(1));
        Assert.That(result.OpenPriorSiblings[0].Id, Is.EqualTo(parent1.Id));
        Assert.That(result.OpenChildren, Has.Count.EqualTo(1));
        Assert.That(result.OpenChildren[0].Id, Is.EqualTo(child1.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_ParallelParent_DoesNotBlockOnPriorSiblings()
    {
        // Arrange - create a parent with Parallel execution mode and two open children
        var parent = await _service.CreateIssueAsync(_tempDir, "Parallel Parent", IssueType.Feature,
            executionMode: ExecutionMode.Parallel);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Open)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Open)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - check blocking issues for child2 (which has an open prior sibling child1)
        var result = await _service.GetBlockingIssuesAsync(_tempDir, child2.Id);

        // Assert - should NOT be blocked because parent is Parallel
        Assert.That(result.OpenPriorSiblings, Is.Empty);
        Assert.That(result.IsBlocked, Is.False);
    }

    [Test]
    public async Task GetBlockingIssuesAsync_SeriesParent_BlocksOnPriorSiblings()
    {
        // Arrange - create a parent with Series execution mode (default) and two open children
        var parent = await _service.CreateIssueAsync(_tempDir, "Series Parent", IssueType.Feature,
            executionMode: ExecutionMode.Series);
        var child1 = await _service.CreateIssueAsync(_tempDir, "Child 1 (Open)", IssueType.Task);
        var child2 = await _service.CreateIssueAsync(_tempDir, "Child 2 (Open)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, child1.Id, parent.Id);
        await _service.AddParentAsync(_tempDir, child2.Id, parent.Id);

        // Act - check blocking issues for child2
        var result = await _service.GetBlockingIssuesAsync(_tempDir, child2.Id);

        // Assert - should be blocked because parent is Series
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenPriorSiblings, Has.Count.EqualTo(1));
        Assert.That(result.OpenPriorSiblings[0].Id, Is.EqualTo(child1.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_ParallelParent_StillBlocksOnOpenChildren()
    {
        // Arrange - issue under a parallel parent still blocked by its own open children
        var grandparent = await _service.CreateIssueAsync(_tempDir, "Grandparent", IssueType.Feature,
            executionMode: ExecutionMode.Parallel);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Task);
        var child = await _service.CreateIssueAsync(_tempDir, "Child (Open)", IssueType.Task);

        await _service.AddParentAsync(_tempDir, parent.Id, grandparent.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act - check blocking issues for parent (has an open child)
        var result = await _service.GetBlockingIssuesAsync(_tempDir, parent.Id);

        // Assert - should be blocked by open child, even though grandparent is Parallel
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenChildren, Has.Count.EqualTo(1));
        Assert.That(result.OpenChildren[0].Id, Is.EqualTo(child.Id));
    }

    [Test]
    public async Task GetBlockingIssuesAsync_MixedParents_OnlyBlocksForSeriesParents()
    {
        // Arrange - issue has two parents: one Parallel and one Series
        var parallelParent = await _service.CreateIssueAsync(_tempDir, "Parallel Parent", IssueType.Feature,
            executionMode: ExecutionMode.Parallel);
        var seriesParent = await _service.CreateIssueAsync(_tempDir, "Series Parent", IssueType.Feature,
            executionMode: ExecutionMode.Series);

        var siblingUnderParallel = await _service.CreateIssueAsync(_tempDir, "Parallel Sibling", IssueType.Task);
        var siblingUnderSeries = await _service.CreateIssueAsync(_tempDir, "Series Sibling", IssueType.Task);
        var target = await _service.CreateIssueAsync(_tempDir, "Target Issue", IssueType.Task);

        // target has prior siblings under both parents
        await _service.AddParentAsync(_tempDir, siblingUnderParallel.Id, parallelParent.Id);
        await _service.AddParentAsync(_tempDir, target.Id, parallelParent.Id);

        await _service.AddParentAsync(_tempDir, siblingUnderSeries.Id, seriesParent.Id);
        await _service.AddParentAsync(_tempDir, target.Id, seriesParent.Id);

        // Act
        var result = await _service.GetBlockingIssuesAsync(_tempDir, target.Id);

        // Assert - only the series parent's prior sibling should block
        Assert.That(result.IsBlocked, Is.True);
        Assert.That(result.OpenPriorSiblings, Has.Count.EqualTo(1));
        Assert.That(result.OpenPriorSiblings[0].Id, Is.EqualTo(siblingUnderSeries.Id));
    }

    #endregion

    #region RemoveParentAsync Tests

    [Test]
    public async Task RemoveParentAsync_RemovesSpecificParent()
    {
        // Arrange - create child with two parents
        var parentA = await _service.CreateIssueAsync(_tempDir, "Parent A", IssueType.Feature);
        var parentB = await _service.CreateIssueAsync(_tempDir, "Parent B", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parentA.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parentB.Id);

        // Act - remove parent A
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parentA.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parentB.Id));
    }

    [Test]
    public void RemoveParentAsync_ThrowsWhenChildNotFound()
    {
        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveParentAsync(_tempDir, "non-existent", "some-parent"));
    }

    #endregion

    #region RemoveAllParentsAsync Tests

    [Test]
    public async Task RemoveAllParentsAsync_RemovesAllParents()
    {
        // Arrange - create child with two parents
        var parentA = await _service.CreateIssueAsync(_tempDir, "Parent A", IssueType.Feature);
        var parentB = await _service.CreateIssueAsync(_tempDir, "Parent B", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parentA.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parentB.Id);

        // Act
        var updated = await _service.RemoveAllParentsAsync(_tempDir, child.Id);

        // Assert
        Assert.That(updated.ParentIssues, Is.Empty);
    }

    [Test]
    public void RemoveAllParentsAsync_ThrowsWhenIssueNotFound()
    {
        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveAllParentsAsync(_tempDir, "non-existent"));
    }

    [Test]
    public async Task RemoveAllParentsAsync_UpdatesCache()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        await _service.RemoveAllParentsAsync(_tempDir, child.Id);

        // Assert - verify cache reflects the change
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Is.Empty);
    }

    [Test]
    public async Task RemoveAllParentsAsync_RecordsHistorySnapshot()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent", IssueType.Feature);
        var child = await _service.CreateIssueAsync(_tempDir, "Child", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);
        _mockHistoryService.Invocations.Clear();

        // Act
        await _service.RemoveAllParentsAsync(_tempDir, child.Id);

        // Assert
        _mockHistoryService.Verify(
            h => h.RecordSnapshotAsync(
                _tempDir,
                It.IsAny<IReadOnlyList<Issue>>(),
                "RemoveAllParents",
                child.Id,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
