using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<FleeceService>> _mockLogger = null!;
    private Mock<IIssueSerializationQueue> _mockQueue = null!;
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

        _service = new FleeceService(_mockQueue.Object, _mockLogger.Object);
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

    #region CreateIssueAsync Tests

    [Test]
    public async Task CreateIssueAsync_WithTitleAndType_CreatesIssue()
    {
        // Arrange
        var title = "Test Issue";
        var type = IssueType.Feature;

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, title, type);

        // Assert
        Assert.That(issue, Is.Not.Null);
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
    }

    [Test]
    public async Task CreateIssueAsync_WithDescription_SetsDescription()
    {
        // Arrange
        var description = "This is a detailed description";

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Task,
            description: description);

        // Assert
        Assert.That(issue.Description, Is.EqualTo(description));
    }

    [Test]
    public async Task CreateIssueAsync_WithPriority_SetsPriority()
    {
        // Arrange
        var priority = 3;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Bug,
            priority: priority);

        // Assert
        Assert.That(issue.Priority, Is.EqualTo(priority));
    }

    [Test]
    public async Task CreateIssueAsync_WithExecutionMode_SetsExecutionMode()
    {
        // Arrange
        var executionMode = ExecutionMode.Parallel;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature,
            executionMode: executionMode);

        // Assert
        Assert.That(issue.ExecutionMode, Is.EqualTo(executionMode));
    }

    [Test]
    public async Task CreateIssueAsync_WithNullExecutionMode_DefaultsToSeries()
    {
        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Test Issue",
            IssueType.Feature);

        // Assert
        Assert.That(issue.ExecutionMode, Is.EqualTo(ExecutionMode.Series));
    }

    [Test]
    public async Task CreateIssueAsync_WithExecutionModeAndAllOtherParams_SetsAllCorrectly()
    {
        // Arrange
        var title = "Full Feature Issue";
        var type = IssueType.Feature;
        var description = "A complete issue";
        var priority = 2;
        var executionMode = ExecutionMode.Parallel;

        // Act
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            title,
            type,
            description: description,
            priority: priority,
            executionMode: executionMode);

        // Assert
        Assert.That(issue.Title, Is.EqualTo(title));
        Assert.That(issue.Type, Is.EqualTo(type));
        Assert.That(issue.Description, Is.EqualTo(description));
        Assert.That(issue.Priority, Is.EqualTo(priority));
        Assert.That(issue.ExecutionMode, Is.EqualTo(executionMode));
    }

    [Test]
    public async Task CreateIssueAsync_IssueCanBeRetrieved()
    {
        // Arrange
        var executionMode = ExecutionMode.Parallel;
        var issue = await _service.CreateIssueAsync(
            _tempDir,
            "Retrievable Issue",
            IssueType.Task,
            executionMode: executionMode);

        // Act
        var retrieved = await _service.GetIssueAsync(_tempDir, issue.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ExecutionMode, Is.EqualTo(executionMode));
    }

    #endregion

    #region Queue Integration Tests

    [Test]
    public async Task CreateIssueAsync_EnqueuesWriteOperation()
    {
        // Act
        await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Create),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task UpdateIssueAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Act
        await _service.UpdateIssueAsync(_tempDir, issue.Id, title: "Updated Title");

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Update && op.IssueId == issue.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DeleteIssueAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Act
        await _service.DeleteIssueAsync(_tempDir, issue.Id);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Delete && op.IssueId == issue.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateIssueAsync_ReturnsImmediately_WithoutWaitingForQueueProcessing()
    {
        // Arrange - queue that simulates slow processing
        var queueCalled = false;
        _mockQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<IssueWriteOperation>(), It.IsAny<CancellationToken>()))
            .Callback(() => queueCalled = true)
            .Returns(ValueTask.CompletedTask);

        // Act
        var issue = await _service.CreateIssueAsync(_tempDir, "Test Issue", IssueType.Task);

        // Assert - issue should be returned and queue should have been called
        Assert.That(issue, Is.Not.Null);
        Assert.That(queueCalled, Is.True);
    }

    [Test]
    public async Task GetIssueAsync_ReturnsFromCache_AfterCreate()
    {
        // Arrange - create an issue
        var created = await _service.CreateIssueAsync(_tempDir, "Cached Issue", IssueType.Feature);

        // Act - retrieve immediately from cache (no disk I/O needed)
        var retrieved = await _service.GetIssueAsync(_tempDir, created.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
        Assert.That(retrieved.Title, Is.EqualTo("Cached Issue"));
    }

    [Test]
    public async Task UpdateIssueAsync_UpdatesCache_BeforePersistence()
    {
        // Arrange
        var created = await _service.CreateIssueAsync(_tempDir, "Original Title", IssueType.Task);

        // Act - update the issue
        var updated = await _service.UpdateIssueAsync(_tempDir, created.Id, title: "Updated Title");

        // Assert - reading from cache should return the updated version
        var retrieved = await _service.GetIssueAsync(_tempDir, created.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task UpdateIssueAsync_NonExistentIssue_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateIssueAsync(_tempDir, "non-existent-id", title: "New Title");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ReloadFromDiskAsync Tests

    [Test]
    public async Task ReloadFromDiskAsync_ClearsAndReloadsCache()
    {
        // Arrange - create an issue through the service (writes to disk and populates cache)
        var issue = await _service.CreateIssueAsync(_tempDir, "Original Title", IssueType.Task);

        // Verify it's in the cache
        var cached = await _service.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.Title, Is.EqualTo("Original Title"));

        // Modify the issue directly on disk by finding and editing the JSONL file
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        Assert.That(issueFiles, Has.Length.GreaterThan(0), "Expected at least one issues JSONL file on disk");

        var issueFile = issueFiles[0];
        var lines = await File.ReadAllLinesAsync(issueFile);
        var updatedLines = lines.Select(line =>
            line.Contains(issue.Id)
                ? line.Replace("Original Title", "Modified On Disk")
                : line
        ).ToArray();
        await File.WriteAllLinesAsync(issueFile, updatedLines);

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - cache should now reflect the on-disk changes
        var reloaded = await _service.GetIssueAsync(_tempDir, issue.Id);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Title, Is.EqualTo("Modified On Disk"));
    }

    [Test]
    public async Task ReloadFromDiskAsync_PicksUpExternallyAddedIssues()
    {
        // Arrange - create an initial issue to establish the .fleece directory and file
        var existingIssue = await _service.CreateIssueAsync(_tempDir, "Existing Issue", IssueType.Task);

        // Find the issues JSONL file on disk
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        Assert.That(issueFiles, Has.Length.GreaterThan(0));

        var issueFile = issueFiles[0];

        // Read an existing line to use as a template, then create a new issue line
        var lines = await File.ReadAllLinesAsync(issueFile);
        var templateLine = lines.First(l => l.Contains(existingIssue.Id));

        // Create a new issue entry by modifying the template
        var newIssueLine = templateLine
            .Replace(existingIssue.Id, "EXT001")
            .Replace("Existing Issue", "Externally Added Issue");
        await File.AppendAllTextAsync(issueFile, newIssueLine + Environment.NewLine);

        // Verify the external issue is NOT visible before reload
        var beforeReload = await _service.GetIssueAsync(_tempDir, "EXT001");
        Assert.That(beforeReload, Is.Null, "External issue should not be in cache before reload");

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - the externally added issue should now be visible
        var afterReload = await _service.GetIssueAsync(_tempDir, "EXT001");
        Assert.That(afterReload, Is.Not.Null, "External issue should be visible after reload");
        Assert.That(afterReload!.Title, Is.EqualTo("Externally Added Issue"));

        // Original issue should still be there
        var original = await _service.GetIssueAsync(_tempDir, existingIssue.Id);
        Assert.That(original, Is.Not.Null);
    }

    [Test]
    public async Task ReloadFromDiskAsync_PicksUpExternallyDeletedIssues()
    {
        // Arrange - create two issues through the service
        var issue1 = await _service.CreateIssueAsync(_tempDir, "Issue One", IssueType.Task);
        var issue2 = await _service.CreateIssueAsync(_tempDir, "Issue Two", IssueType.Bug);

        // Verify both are in cache
        Assert.That(await _service.GetIssueAsync(_tempDir, issue1.Id), Is.Not.Null);
        Assert.That(await _service.GetIssueAsync(_tempDir, issue2.Id), Is.Not.Null);

        // Remove issue1 from disk by filtering it out of the JSONL file
        var issueFiles = Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues_*.jsonl")
            .Concat(Directory.GetFiles(Path.Combine(_tempDir, ".fleece"), "issues.jsonl"))
            .ToArray();
        var issueFile = issueFiles[0];
        var lines = await File.ReadAllLinesAsync(issueFile);
        var filteredLines = lines.Where(line => !line.Contains(issue1.Id)).ToArray();
        await File.WriteAllLinesAsync(issueFile, filteredLines);

        // Act - reload from disk
        await _service.ReloadFromDiskAsync(_tempDir);

        // Assert - issue1 should no longer be in cache, issue2 should still be there
        var reloadedIssue1 = await _service.GetIssueAsync(_tempDir, issue1.Id);
        Assert.That(reloadedIssue1, Is.Null, "Deleted issue should not be in cache after reload");

        var reloadedIssue2 = await _service.GetIssueAsync(_tempDir, issue2.Id);
        Assert.That(reloadedIssue2, Is.Not.Null, "Remaining issue should still be in cache after reload");
        Assert.That(reloadedIssue2!.Title, Is.EqualTo("Issue Two"));
    }

    #endregion

    #region AddParentAsync Tests

    [Test]
    public async Task AddParentAsync_CreatesParentRelationship()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task AddParentAsync_UpdatesCache()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert - verify cache is updated
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(retrieved.ParentIssues[0].ParentIssue, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task AddParentAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        _mockQueue.Invocations.Clear();

        // Act
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Update && op.IssueId == child.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task AddParentAsync_NonExistentChild_ThrowsKeyNotFoundException()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.AddParentAsync(_tempDir, "non-existent-id", parent.Id));
    }

    [Test]
    public async Task AddParentAsync_CanAddMultipleParents()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Task);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);

        // Act
        await _service.AddParentAsync(_tempDir, child.Id, parent1.Id);
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent2.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(2));
        var parentIds = updated.ParentIssues.Select(p => p.ParentIssue).ToList();
        Assert.That(parentIds, Does.Contain(parent1.Id));
        Assert.That(parentIds, Does.Contain(parent2.Id));
    }

    [Test]
    public async Task AddParentAsync_WithSortOrder_UsesSortOrder()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent.Id, sortOrder: "0V");

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].SortOrder, Is.EqualTo("0V"));
    }

    [Test]
    public async Task AddParentAsync_WithoutSortOrder_DefaultsToZero()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act
        var updated = await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].SortOrder, Is.EqualTo("0"));
    }

    #endregion

    #region RemoveParentAsync Tests

    [Test]
    public async Task RemoveParentAsync_RemovesParentRelationship()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated.ParentIssues, Is.Empty);
    }

    [Test]
    public async Task RemoveParentAsync_UpdatesCache()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);

        // Act
        await _service.RemoveParentAsync(_tempDir, child.Id, parent.Id);

        // Assert - verify cache is updated
        var retrieved = await _service.GetIssueAsync(_tempDir, child.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ParentIssues, Is.Empty);
    }

    [Test]
    public async Task RemoveParentAsync_EnqueuesWriteOperation()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);
        await _service.AddParentAsync(_tempDir, child.Id, parent.Id);
        _mockQueue.Invocations.Clear();

        // Act
        await _service.RemoveParentAsync(_tempDir, child.Id, parent.Id);

        // Assert
        _mockQueue.Verify(
            q => q.EnqueueAsync(
                It.Is<IssueWriteOperation>(op => op.Type == WriteOperationType.Update && op.IssueId == child.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RemoveParentAsync_NonExistentChild_ThrowsKeyNotFoundException()
    {
        // Arrange
        var parent = await _service.CreateIssueAsync(_tempDir, "Parent Issue", IssueType.Task);

        // Act & Assert
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _service.RemoveParentAsync(_tempDir, "non-existent-id", parent.Id));
    }

    [Test]
    public async Task RemoveParentAsync_KeepsOtherParents()
    {
        // Arrange
        var child = await _service.CreateIssueAsync(_tempDir, "Child Issue", IssueType.Task);
        var parent1 = await _service.CreateIssueAsync(_tempDir, "Parent 1", IssueType.Task);
        var parent2 = await _service.CreateIssueAsync(_tempDir, "Parent 2", IssueType.Feature);
        await _service.AddParentAsync(_tempDir, child.Id, parent1.Id);
        await _service.AddParentAsync(_tempDir, child.Id, parent2.Id);

        // Act
        var updated = await _service.RemoveParentAsync(_tempDir, child.Id, parent1.Id);

        // Assert
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parent2.Id));
    }

    #endregion
}
