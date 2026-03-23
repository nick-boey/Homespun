using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class QueueCoordinatorTests
{
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IAgentStartBackgroundService> _mockAgentStartService = null!;
    private Mock<IHubContext<NotificationHub>> _mockNotificationHub = null!;
    private Mock<ILogger<QueueCoordinator>> _mockLogger = null!;
    private Mock<ILoggerFactory> _mockLoggerFactory = null!;
    private QueueCoordinator _coordinator = null!;
    private List<QueueCoordinatorEvent> _emittedEvents = null!;

    private const string ProjectId = "proj1";
    private const string ProjectPath = "/path/to/project";
    private const string DefaultBranch = "main";
    private const int DefaultMaxConcurrency = 5;

    [SetUp]
    public void SetUp()
    {
        _mockFleeceService = new Mock<IFleeceService>();
        _mockAgentStartService = new Mock<IAgentStartBackgroundService>();
        _mockNotificationHub = new Mock<IHubContext<NotificationHub>>();
        _mockLogger = new Mock<ILogger<QueueCoordinator>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        // Set up SignalR mock to avoid null reference
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _mockNotificationHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _coordinator = new QueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            DefaultMaxConcurrency);

        _emittedEvents = new List<QueueCoordinatorEvent>();
        _coordinator.OnEvent += e => _emittedEvents.Add(e);
    }

    private Issue CreateIssue(
        string id,
        ExecutionMode executionMode = ExecutionMode.Series,
        IssueStatus status = IssueStatus.Open)
    {
        return new Issue
        {
            Id = id,
            Title = $"Test Issue {id}",
            Status = status,
            Type = IssueType.Task,
            ExecutionMode = executionMode,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private void SetupIssueWithChildren(
        string parentId,
        ExecutionMode parentMode,
        params string[] childIds)
    {
        var parentIssue = CreateIssue(parentId, parentMode);
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentIssue);

        var children = childIds.Select(id => CreateIssue(id)).ToList();
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children);

        // Set up each child as having no children by default
        foreach (var childId in childIds)
        {
            _mockFleeceService
                .Setup(f => f.GetIssueAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(children.First(c => c.Id == childId));
            _mockFleeceService
                .Setup(f => f.GetChildrenAsync(ProjectPath, childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Issue>());
        }
    }

    #region Initial State

    [Test]
    public void GetStatus_ReturnsNull_WhenNoExecutionStarted()
    {
        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Null);
    }

    [Test]
    public void GetActiveQueues_ReturnsEmpty_WhenNoExecutionStarted()
    {
        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Is.Empty);
    }

    #endregion

    #region Series Mode

    [Test]
    public async Task StartExecution_SeriesMode_CreatesSingleQueue()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task StartExecution_SeriesMode_EnqueuesChildrenInOrder()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queue = _coordinator.GetActiveQueues(ProjectId)[0];

        // First child should be current (running)
        Assert.That(queue.CurrentRequest?.IssueId, Is.EqualTo("child1"));
        // Remaining children should be pending in order
        Assert.That(queue.PendingRequests.Select(r => r.IssueId),
            Is.EqualTo(new[] { "child2", "child3" }));
    }

    [Test]
    public async Task StartExecution_SeriesMode_EmitsExecutionStartedEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        Assert.That(_emittedEvents, Has.Some.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.ExecutionStarted
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task StartExecution_SeriesMode_StatusIsRunning()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.Status, Is.EqualTo(QueueCoordinatorStatus.Running));
        Assert.That(status.RootIssueId, Is.EqualTo("root"));
    }

    #endregion

    #region Parallel Mode

    [Test]
    public async Task StartExecution_ParallelMode_CreatesMultipleQueues()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task StartExecution_ParallelMode_EachQueueHasOneChild()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2", "child3");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var currentIssueIds = queues
            .Select(q => q.CurrentRequest?.IssueId)
            .OrderBy(id => id)
            .ToList();

        Assert.That(currentIssueIds, Is.EqualTo(new[] { "child1", "child2", "child3" }));
        Assert.That(queues.All(q => q.PendingRequests.Count == 0), Is.True);
    }

    [Test]
    public async Task StartExecution_ParallelMode_EmitsQueueCreatedEvents()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queueCreatedEvents = _emittedEvents
            .Where(e => e.EventType == QueueCoordinatorEventType.QueueCreated)
            .ToList();

        Assert.That(queueCreatedEvents, Has.Count.EqualTo(2));
    }

    #endregion

    #region Max Concurrency Enforcement

    [Test]
    public async Task StartExecution_ParallelMode_RespectsMaxConcurrency()
    {
        var coordinator = new QueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            maxConcurrency: 2);

        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            "child1", "child2", "child3", "child4");

        await coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var status = coordinator.GetStatus(ProjectId);
        Assert.That(status, Is.Not.Null);
        // Only 2 queues should be actively running
        Assert.That(status!.RunningQueueCount, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task StartExecution_MaxConcurrency_AllQueuesCreated()
    {
        var coordinator = new QueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            maxConcurrency: 2);

        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            "child1", "child2", "child3", "child4");

        await coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = coordinator.GetActiveQueues(ProjectId);
        // All queues are created, but only maxConcurrency are running
        Assert.That(queues, Has.Count.EqualTo(4));
    }

    #endregion

    #region Nested Hierarchies

    [Test]
    public async Task StartExecution_SeriesWithinParallel_CreatesCorrectStructure()
    {
        // root (parallel) -> child1 (series: grandchild1, grandchild2), child2 (leaf)
        var rootIssue = CreateIssue("root", ExecutionMode.Parallel);
        var child1Issue = CreateIssue("child1", ExecutionMode.Series);
        var child2Issue = CreateIssue("child2", ExecutionMode.Series);
        var grandchild1 = CreateIssue("gc1");
        var grandchild2 = CreateIssue("gc2");

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { child1Issue, child2Issue });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child1Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { grandchild1, grandchild2 });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child2Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        // Set up grandchildren as leaf nodes
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchild1);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandchild2);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Parallel root creates 2 queues
        Assert.That(queues, Has.Count.EqualTo(2));

        // One queue should have the series children (gc1, gc2)
        var seriesQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "gc1");
        Assert.That(seriesQueue, Is.Not.Null);
        Assert.That(seriesQueue!.PendingRequests.Select(r => r.IssueId),
            Is.EqualTo(new[] { "gc2" }));

        // Other queue should have child2 as a leaf
        var leafQueue = queues.FirstOrDefault(q => q.CurrentRequest?.IssueId == "child2");
        Assert.That(leafQueue, Is.Not.Null);
    }

    [Test]
    public async Task StartExecution_ParallelWithinSeries_CreatesCorrectStructure()
    {
        // root (series) -> child1 (parallel: gc1, gc2), child2 (leaf)
        // Series root: one queue with child1 first. Since child1 has children,
        // the coordinator should expand child1's children into the queue.
        var rootIssue = CreateIssue("root", ExecutionMode.Series);
        var child1Issue = CreateIssue("child1", ExecutionMode.Parallel);
        var child2Issue = CreateIssue("child2", ExecutionMode.Series);
        var gc1 = CreateIssue("gc1");
        var gc2 = CreateIssue("gc2");

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "root", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { child1Issue, child2Issue });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child1Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { gc1, gc2 });

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child2Issue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "child2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(gc1);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(gc2);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "gc2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        // Series root creates 1 queue initially for the first series child.
        // child1 is parallel, so it should spawn parallel sub-queues for gc1 and gc2.
        // child2 is deferred until child1's parallel group completes.
        var queues = _coordinator.GetActiveQueues(ProjectId);
        // Should have 2 queues for gc1 and gc2 (parallel expansion of child1)
        Assert.That(queues, Has.Count.EqualTo(2));
    }

    #endregion

    #region Completion Signaling

    [Test]
    public async Task AllQueuesComplete_EmitsAllQueuesCompletedEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _emittedEvents.Clear();

        // Simulate the queue completing by notifying through the queue
        var queues = _coordinator.GetActiveQueues(ProjectId);
        var queue = (TaskQueue)queues[0];
        queue.NotifyCompleted("child1", success: true);

        Assert.That(_emittedEvents, Has.Some.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.AllQueuesCompleted
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task AllQueuesComplete_StatusIsCompleted()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queue = (TaskQueue)_coordinator.GetActiveQueues(ProjectId)[0];
        queue.NotifyCompleted("child1", success: true);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(QueueCoordinatorStatus.Completed));
    }

    [Test]
    public async Task ParallelQueues_AllMustComplete_BeforeSignalingDone()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var queue1 = (TaskQueue)queues.First(q => q.CurrentRequest?.IssueId == "child1");
        var queue2 = (TaskQueue)queues.First(q => q.CurrentRequest?.IssueId == "child2");

        _emittedEvents.Clear();

        // Complete first queue - should not signal all done yet
        queue1.NotifyCompleted("child1", success: true);

        Assert.That(_emittedEvents, Has.None.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.AllQueuesCompleted));

        // Complete second queue - now should signal all done
        queue2.NotifyCompleted("child2", success: true);

        Assert.That(_emittedEvents, Has.Some.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.AllQueuesCompleted));
    }

    #endregion

    #region CancelAll

    [Test]
    public async Task CancelAll_CancelsAllQueues()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel, "child1", "child2");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _coordinator.CancelAll(ProjectId);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues.All(q => q.State == TaskQueueState.Completed), Is.True);
    }

    [Test]
    public async Task CancelAll_EmitsExecutionCancelledEvent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _emittedEvents.Clear();

        _coordinator.CancelAll(ProjectId);

        Assert.That(_emittedEvents, Has.Some.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.ExecutionCancelled
                 && e.ProjectId == ProjectId));
    }

    [Test]
    public async Task CancelAll_StatusIsCancelled()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        _coordinator.CancelAll(ProjectId);

        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(QueueCoordinatorStatus.Cancelled));
    }

    [Test]
    public void CancelAll_NoOpWhenNoExecution()
    {
        // Should not throw
        Assert.DoesNotThrow(() => _coordinator.CancelAll(ProjectId));
    }

    #endregion

    #region Leaf Issue (No Children)

    [Test]
    public async Task StartExecution_LeafIssue_CreatesSingleQueueWithIssue()
    {
        var leafIssue = CreateIssue("leaf");
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "leaf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(leafIssue);
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, "leaf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

        await _coordinator.StartExecution(ProjectId, "leaf",
            ProjectPath, DefaultBranch);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("leaf"));
    }

    #endregion

    #region Issue Not Found

    [Test]
    public void StartExecution_IssueNotFound_Throws()
    {
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null);

        Assert.ThrowsAsync<KeyNotFoundException>(
            () => _coordinator.StartExecution(ProjectId, "missing",
                ProjectPath, DefaultBranch));
    }

    #endregion

    #region SignalR Broadcasting

    [Test]
    public async Task StartExecution_BroadcastsQueueStatusChanges()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series, "child1");

        await _coordinator.StartExecution(ProjectId, "root",
            ProjectPath, DefaultBranch);

        // Verify SignalR broadcasting was called
        _mockNotificationHub.Verify(
            h => h.Clients, Times.AtLeastOnce);
    }

    #endregion
}
