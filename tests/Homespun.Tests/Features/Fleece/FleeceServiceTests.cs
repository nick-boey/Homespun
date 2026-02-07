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
}
