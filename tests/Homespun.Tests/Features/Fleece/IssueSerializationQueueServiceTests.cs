using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class IssueSerializationQueueServiceTests
{
    private Mock<ILogger<IssueSerializationQueueService>> _mockLogger = null!;
    private IssueSerializationQueueService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<IssueSerializationQueueService>>();
        _service = new IssueSerializationQueueService(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    #region Enqueue Tests

    [Test]
    public async Task EnqueueAsync_IncreasesPendingCount()
    {
        // Arrange
        var operation = CreateTestOperation();

        // Act
        await _service.EnqueueAsync(operation);

        // Assert
        Assert.That(_service.PendingCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnqueueAsync_MultipleTimes_TracksPendingCount()
    {
        // Act
        await _service.EnqueueAsync(CreateTestOperation("issue-1"));
        await _service.EnqueueAsync(CreateTestOperation("issue-2"));
        await _service.EnqueueAsync(CreateTestOperation("issue-3"));

        // Assert
        Assert.That(_service.PendingCount, Is.EqualTo(3));
    }

    #endregion

    #region Processing Tests

    [Test]
    public async Task ExecuteAsync_ProcessesQueuedOperations()
    {
        // Arrange
        var processed = false;
        var operation = new IssueWriteOperation(
            ProjectPath: "/test/path",
            IssueId: "test-id",
            Type: WriteOperationType.Create,
            WriteAction: async (ct) =>
            {
                processed = true;
                await Task.CompletedTask;
            },
            QueuedAt: DateTimeOffset.UtcNow
        );

        using var cts = new CancellationTokenSource();

        // Start the service in the background
        var executeTask = _service.StartAsync(cts.Token);

        // Act
        await _service.EnqueueAsync(operation);

        // Wait for processing with timeout
        await WaitForConditionAsync(() => processed, TimeSpan.FromSeconds(5));

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(processed, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_ProcessesOperationsInOrder()
    {
        // Arrange
        var processedOrder = new List<string>();
        var allProcessed = new TaskCompletionSource<bool>();

        IssueWriteOperation CreateOrderedOp(string id, int totalExpected) => new(
            ProjectPath: "/test/path",
            IssueId: id,
            Type: WriteOperationType.Create,
            WriteAction: async (ct) =>
            {
                lock (processedOrder)
                {
                    processedOrder.Add(id);
                    if (processedOrder.Count == totalExpected)
                        allProcessed.TrySetResult(true);
                }
                await Task.CompletedTask;
            },
            QueuedAt: DateTimeOffset.UtcNow
        );

        using var cts = new CancellationTokenSource();

        // Enqueue operations before starting the service to guarantee order
        await _service.EnqueueAsync(CreateOrderedOp("first", 3));
        await _service.EnqueueAsync(CreateOrderedOp("second", 3));
        await _service.EnqueueAsync(CreateOrderedOp("third", 3));

        // Start the service
        var executeTask = _service.StartAsync(cts.Token);

        // Wait for all to process
        var completed = await Task.WhenAny(allProcessed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(completed, Is.EqualTo(allProcessed.Task), "Timed out waiting for operations to process");

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(processedOrder, Is.EqualTo(new[] { "first", "second", "third" }));
    }

    [Test]
    public async Task ExecuteAsync_DecrementsPendingCount_AfterProcessing()
    {
        // Arrange
        var processingStarted = new TaskCompletionSource<bool>();
        var operation = new IssueWriteOperation(
            ProjectPath: "/test/path",
            IssueId: "test-id",
            Type: WriteOperationType.Create,
            WriteAction: async (ct) =>
            {
                processingStarted.TrySetResult(true);
                await Task.CompletedTask;
            },
            QueuedAt: DateTimeOffset.UtcNow
        );

        using var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        // Act
        await _service.EnqueueAsync(operation);
        await Task.WhenAny(processingStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // Give a moment for the count to decrement
        await WaitForConditionAsync(() => _service.PendingCount == 0, TimeSpan.FromSeconds(5));

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(_service.PendingCount, Is.EqualTo(0));
    }

    #endregion

    #region Retry Tests

    [Test]
    public async Task ExecuteAsync_RetriesOnFailure()
    {
        // Arrange
        var attemptCount = 0;
        var operation = new IssueWriteOperation(
            ProjectPath: "/test/path",
            IssueId: "test-id",
            Type: WriteOperationType.Update,
            WriteAction: async (ct) =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new IOException("Simulated I/O failure");
                }
                await Task.CompletedTask;
            },
            QueuedAt: DateTimeOffset.UtcNow
        );

        using var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        // Act
        await _service.EnqueueAsync(operation);
        await WaitForConditionAsync(() => attemptCount >= 3, TimeSpan.FromSeconds(10));

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert - should have retried and eventually succeeded
        Assert.That(attemptCount, Is.GreaterThanOrEqualTo(3));
    }

    #endregion

    #region Graceful Shutdown Tests

    [Test]
    public async Task StopAsync_DrainsRemainingOperations()
    {
        // Arrange
        var processedIds = new List<string>();

        IssueWriteOperation CreateOp(string id) => new(
            ProjectPath: "/test/path",
            IssueId: id,
            Type: WriteOperationType.Create,
            WriteAction: async (ct) =>
            {
                lock (processedIds)
                    processedIds.Add(id);
                await Task.CompletedTask;
            },
            QueuedAt: DateTimeOffset.UtcNow
        );

        // Enqueue operations BEFORE starting the service so they're queued up
        await _service.EnqueueAsync(CreateOp("first"));
        await _service.EnqueueAsync(CreateOp("second"));

        using var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        // Wait for operations to process
        await WaitForConditionAsync(() => processedIds.Count >= 2, TimeSpan.FromSeconds(5));

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert - both operations should have been processed
        Assert.That(processedIds, Does.Contain("first"));
        Assert.That(processedIds, Does.Contain("second"));
        Assert.That(_service.PendingCount, Is.EqualTo(0));
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public async Task EnqueueAsync_ConcurrentEnqueue_IsThreadSafe()
    {
        // Arrange
        var processedCount = 0;
        var allDone = new TaskCompletionSource<bool>();
        const int totalOps = 50;

        using var cts = new CancellationTokenSource();
        var executeTask = _service.StartAsync(cts.Token);

        // Act - enqueue concurrently from multiple threads
        var enqueueTasks = Enumerable.Range(0, totalOps).Select(i =>
        {
            return _service.EnqueueAsync(new IssueWriteOperation(
                ProjectPath: "/test/path",
                IssueId: $"issue-{i}",
                Type: WriteOperationType.Create,
                WriteAction: async (ct) =>
                {
                    var count = Interlocked.Increment(ref processedCount);
                    if (count == totalOps)
                        allDone.TrySetResult(true);
                    await Task.CompletedTask;
                },
                QueuedAt: DateTimeOffset.UtcNow
            )).AsTask();
        });

        await Task.WhenAll(enqueueTasks);

        // Wait for all to be processed
        var completed = await Task.WhenAny(allDone.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(completed, Is.EqualTo(allDone.Task), "Timed out waiting for all operations to process");

        // Stop the service
        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(processedCount, Is.EqualTo(totalOps));
    }

    #endregion

    #region Helpers

    private static IssueWriteOperation CreateTestOperation(string issueId = "test-id")
    {
        return new IssueWriteOperation(
            ProjectPath: "/test/path",
            IssueId: issueId,
            Type: WriteOperationType.Create,
            WriteAction: async (ct) => await Task.CompletedTask,
            QueuedAt: DateTimeOffset.UtcNow
        );
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    #endregion
}
