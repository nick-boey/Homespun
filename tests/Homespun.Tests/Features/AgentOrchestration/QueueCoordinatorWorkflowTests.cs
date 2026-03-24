using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class QueueCoordinatorWorkflowTests
{
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IAgentStartBackgroundService> _mockAgentStartService = null!;
    private Mock<IWorkflowExecutionService> _mockWorkflowService = null!;
    private Mock<IHubContext<NotificationHub>> _mockNotificationHub = null!;
    private Mock<ILogger<QueueCoordinator>> _mockLogger = null!;
    private Mock<ILoggerFactory> _mockLoggerFactory = null!;
    private QueueCoordinator _coordinator = null!;
    private List<QueueCoordinatorEvent> _emittedEvents = null!;

    private const string ProjectId = "proj1";
    private const string ProjectPath = "/path/to/project";
    private const string DefaultBranch = "main";

    [SetUp]
    public void SetUp()
    {
        _mockFleeceService = new Mock<IFleeceService>();
        _mockAgentStartService = new Mock<IAgentStartBackgroundService>();
        _mockWorkflowService = new Mock<IWorkflowExecutionService>();
        _mockNotificationHub = new Mock<IHubContext<NotificationHub>>();
        _mockLogger = new Mock<ILogger<QueueCoordinator>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _mockNotificationHub.Setup(h => h.Clients).Returns(mockClients.Object);

        _coordinator = new QueueCoordinator(
            _mockFleeceService.Object,
            _mockAgentStartService.Object,
            _mockWorkflowService.Object,
            _mockNotificationHub.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            maxConcurrency: 5);

        _emittedEvents = new List<QueueCoordinatorEvent>();
        _coordinator.OnEvent += e => _emittedEvents.Add(e);
    }

    private Issue CreateIssue(
        string id,
        IssueType type = IssueType.Task,
        ExecutionMode executionMode = ExecutionMode.Series)
    {
        return new Issue
        {
            Id = id,
            Title = $"Test Issue {id}",
            Status = IssueStatus.Open,
            Type = type,
            ExecutionMode = executionMode,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private void SetupIssueWithChildren(
        string parentId,
        ExecutionMode parentMode,
        params (string id, IssueType type)[] children)
    {
        var parentIssue = CreateIssue(parentId, executionMode: parentMode);
        _mockFleeceService
            .Setup(f => f.GetIssueAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentIssue);

        var childIssues = children.Select(c => CreateIssue(c.id, c.type)).ToList();
        _mockFleeceService
            .Setup(f => f.GetChildrenAsync(ProjectPath, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(childIssues);

        foreach (var child in childIssues)
        {
            _mockFleeceService
                .Setup(f => f.GetIssueAsync(ProjectPath, child.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(child);
            _mockFleeceService
                .Setup(f => f.GetChildrenAsync(ProjectPath, child.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Issue>());
        }
    }

    private void SetupWorkflowStartSuccess(string executionId = "exec-123")
    {
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = executionId,
                    WorkflowId = "wf-build",
                    ProjectId = ProjectId,
                    Trigger = new ExecutionTriggerInfo()
                }
            });
    }

    #region Workflow Mapping in CreateRequest

    [Test]
    public async Task StartExecution_IssueWithMappedWorkflow_StartsWorkflowInsteadOfAgent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task));

        SetupWorkflowStartSuccess();

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(
                ProjectPath,
                "wf-build",
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Never);
    }

    [Test]
    public async Task StartExecution_IssueWithoutMappedWorkflow_FallsBackToDirectAgent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task));

        var workflowMappings = new Dictionary<string, string>
        {
            ["Bug"] = "wf-bugfix" // Only Bug is mapped, not Task
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.Is<AgentStartRequest>(r => r.IssueId == "child1")),
            Times.Once);

        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TriggerContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task StartExecution_NoWorkflowMappings_FallsBackToDirectAgent()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task));

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch);

        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.Is<AgentStartRequest>(r => r.IssueId == "child1")),
            Times.Once);
    }

    [Test]
    public async Task StartExecution_MixedIssueTypes_RoutesCorrectly()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            ("task-child", IssueType.Task),
            ("bug-child", IssueType.Bug));

        var execCount = 0;
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = $"exec-{++execCount}",
                    WorkflowId = "wf-build",
                    ProjectId = ProjectId,
                    Trigger = new ExecutionTriggerInfo()
                }
            });

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
            // Bug is NOT mapped
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        // Task child should use workflow
        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(ProjectPath, "wf-build", It.IsAny<TriggerContext>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Bug child should use direct agent
        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.Is<AgentStartRequest>(r => r.IssueId == "bug-child")),
            Times.Once);
    }

    #endregion

    #region Workflow Completion → Queue Advancement

    [Test]
    public async Task WorkflowCompletion_AdvancesQueueToNextIssue()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task),
            ("child2", IssueType.Task));

        SetupWorkflowStartSuccess("exec-1");

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues, Has.Count.EqualTo(1));
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child1"));

        // Set up next workflow start
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = "exec-2",
                    WorkflowId = "wf-build",
                    ProjectId = ProjectId,
                    Trigger = new ExecutionTriggerInfo()
                }
            });

        // Simulate workflow completion via the event
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-1",
                Success = true
            });

        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child2"));
    }

    [Test]
    public async Task WorkflowFailure_MarksQueueItemFailed()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task));

        SetupWorkflowStartSuccess("exec-1");

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        _emittedEvents.Clear();

        // Simulate workflow failure via the event
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-1",
                Success = false,
                Error = "Step 2 failed"
            });

        var queues = _coordinator.GetActiveQueues(ProjectId);
        var history = queues[0].History;
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].Success, Is.False);
        Assert.That(history[0].Error, Is.EqualTo("Step 2 failed"));
    }

    [Test]
    public async Task MultipleWorkflowExecutions_QueueDrainsCorrectly()
    {
        SetupIssueWithChildren("root", ExecutionMode.Series,
            ("child1", IssueType.Task),
            ("child2", IssueType.Task),
            ("child3", IssueType.Task));

        var execCount = 0;
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = $"exec-{++execCount}",
                    WorkflowId = "wf-build",
                    ProjectId = ProjectId,
                    Trigger = new ExecutionTriggerInfo()
                }
            });

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        var queues = _coordinator.GetActiveQueues(ProjectId);
        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child1"));

        // Complete child1's workflow
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-1",
                Success = true
            });

        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child2"));

        // Complete child2's workflow
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-2",
                Success = true
            });

        Assert.That(queues[0].CurrentRequest?.IssueId, Is.EqualTo("child3"));

        // Complete child3's workflow
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-3",
                Success = true
            });

        // All done
        var status = _coordinator.GetStatus(ProjectId);
        Assert.That(status!.Status, Is.EqualTo(QueueCoordinatorStatus.Completed));
    }

    #endregion

    #region Parallel Workflow Execution

    [Test]
    public async Task ParallelWorkflows_AllMustComplete_BeforeSignalingDone()
    {
        SetupIssueWithChildren("root", ExecutionMode.Parallel,
            ("child1", IssueType.Task),
            ("child2", IssueType.Task));

        var execCount = 0;
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = $"exec-{++execCount}",
                    WorkflowId = "wf-build",
                    ProjectId = ProjectId,
                    Trigger = new ExecutionTriggerInfo()
                }
            });

        var workflowMappings = new Dictionary<string, string>
        {
            ["Task"] = "wf-build"
        };

        await _coordinator.StartExecution(ProjectId, "root", ProjectPath, DefaultBranch, workflowMappings);

        _emittedEvents.Clear();

        // Complete first workflow - should NOT signal all done
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-1",
                Success = true
            });

        Assert.That(_emittedEvents, Has.None.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.AllQueuesCompleted));

        // Complete second workflow - NOW should signal all done
        _mockWorkflowService.Raise(
            w => w.OnExecutionCompleted += null,
            new WorkflowExecutionCompletedEvent
            {
                ProjectPath = ProjectPath,
                ExecutionId = "exec-2",
                Success = true
            });

        Assert.That(_emittedEvents, Has.Some.Matches<QueueCoordinatorEvent>(
            e => e.EventType == QueueCoordinatorEventType.AllQueuesCompleted));
    }

    #endregion
}
