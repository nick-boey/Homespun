using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class TaskQueueWorkflowTests
{
    private Mock<IAgentStartBackgroundService> _mockAgentStartService = null!;
    private Mock<IWorkflowExecutionService> _mockWorkflowService = null!;
    private Mock<ILogger<TaskQueue>> _mockLogger = null!;
    private TaskQueue _queue = null!;
    private List<TaskQueueEvent> _emittedEvents = null!;

    private const string ProjectPath = "/path/to/project";

    [SetUp]
    public void SetUp()
    {
        _mockAgentStartService = new Mock<IAgentStartBackgroundService>();
        _mockWorkflowService = new Mock<IWorkflowExecutionService>();
        _mockLogger = new Mock<ILogger<TaskQueue>>();
        _queue = new TaskQueue(_mockAgentStartService.Object, _mockWorkflowService.Object, _mockLogger.Object);
        _emittedEvents = new List<TaskQueueEvent>();
        _queue.OnEvent += e => _emittedEvents.Add(e);
    }

    private AgentStartRequest CreateRequest(string issueId = "issue1", string? workflowId = null)
    {
        return new AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = "proj1",
            ProjectLocalPath = ProjectPath,
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = issueId,
                Title = $"Test Issue {issueId}",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                LastUpdate = DateTimeOffset.UtcNow
            },
            BranchName = $"task/test-{issueId}+{issueId}",
            WorkflowId = workflowId
        };
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
                    WorkflowId = "wf-1",
                    ProjectId = "proj1",
                    Trigger = new ExecutionTriggerInfo()
                }
            });
    }

    private void SetupWorkflowStartFailure(string error = "Workflow not found")
    {
        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = false,
                Error = error
            });
    }

    #region Workflow Dispatch

    [Test]
    public async Task EnqueueAsync_WithWorkflowId_StartsWorkflowInsteadOfAgent()
    {
        SetupWorkflowStartSuccess();
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await _queue.EnqueueAsync(request);

        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(
                ProjectPath,
                "wf-1",
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.IsAny<AgentStartRequest>()),
            Times.Never);
    }

    [Test]
    public async Task EnqueueAsync_WithoutWorkflowId_StartsAgentDirectly()
    {
        var request = CreateRequest("issue1");

        await _queue.EnqueueAsync(request);

        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(request),
            Times.Once);

        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task EnqueueAsync_WithWorkflowId_PassesIssueContextInTrigger()
    {
        SetupWorkflowStartSuccess();
        TriggerContext? capturedContext = null;

        _mockWorkflowService
            .Setup(w => w.StartWorkflowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TriggerContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TriggerContext, CancellationToken>(
                (_, _, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new StartWorkflowResult
            {
                Success = true,
                Execution = new WorkflowExecution
                {
                    Id = "exec-1",
                    WorkflowId = "wf-1",
                    ProjectId = "proj1",
                    Trigger = new ExecutionTriggerInfo()
                }
            });

        var request = CreateRequest("issue1", workflowId: "wf-1");
        await _queue.EnqueueAsync(request);

        Assert.That(capturedContext, Is.Not.Null);
        Assert.That(capturedContext!.Input["issueId"], Is.EqualTo("issue1"));
        Assert.That(capturedContext.Input["branchName"], Is.EqualTo(request.BranchName));
        Assert.That(capturedContext.Input["projectId"], Is.EqualTo("proj1"));
        Assert.That(capturedContext.TriggeredBy, Is.EqualTo("QueueCoordinator"));
    }

    [Test]
    public async Task EnqueueAsync_WorkflowStartFailure_NotifiesCompletionWithFailure()
    {
        SetupWorkflowStartFailure("Workflow disabled");
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await _queue.EnqueueAsync(request);

        // Queue should transition to Idle after failure
        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.CurrentRequest, Is.Null);

        // History should record the failure
        Assert.That(_queue.History, Has.Count.EqualTo(1));
        Assert.That(_queue.History[0].Success, Is.False);
        Assert.That(_queue.History[0].Error, Is.EqualTo("Workflow disabled"));
    }

    #endregion

    #region Workflow Completion

    [Test]
    public async Task NotifyWorkflowCompleted_AdvancesQueue()
    {
        SetupWorkflowStartSuccess("exec-1");
        var request1 = CreateRequest("issue1", workflowId: "wf-1");
        var request2 = CreateRequest("issue2");

        await _queue.EnqueueAsync(request1);
        await _queue.EnqueueAsync(request2);

        Assert.That(_queue.CurrentRequest?.IssueId, Is.EqualTo("issue1"));
        Assert.That(_queue.PendingRequests, Has.Count.EqualTo(1));

        // Simulate workflow completion
        _queue.NotifyWorkflowCompleted("exec-1", success: true);

        Assert.That(_queue.CurrentRequest?.IssueId, Is.EqualTo("issue2"));
        Assert.That(_queue.History, Has.Count.EqualTo(1));
        Assert.That(_queue.History[0].IssueId, Is.EqualTo("issue1"));
        Assert.That(_queue.History[0].Success, Is.True);
    }

    [Test]
    public async Task NotifyWorkflowCompleted_Failure_RecordsError()
    {
        SetupWorkflowStartSuccess("exec-1");
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await _queue.EnqueueAsync(request);

        _queue.NotifyWorkflowCompleted("exec-1", success: false, error: "Step 2 failed");

        Assert.That(_queue.State, Is.EqualTo(TaskQueueState.Idle));
        Assert.That(_queue.History, Has.Count.EqualTo(1));
        Assert.That(_queue.History[0].Success, Is.False);
        Assert.That(_queue.History[0].Error, Is.EqualTo("Step 2 failed"));
    }

    [Test]
    public async Task NotifyWorkflowCompleted_UnknownExecutionId_DoesNothing()
    {
        SetupWorkflowStartSuccess("exec-1");
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await _queue.EnqueueAsync(request);

        // Unknown execution ID should be ignored
        _queue.NotifyWorkflowCompleted("unknown-exec", success: true);

        // issue1 should still be current
        Assert.That(_queue.CurrentRequest?.IssueId, Is.EqualTo("issue1"));
        Assert.That(_queue.History, Is.Empty);
    }

    [Test]
    public async Task NotifyWorkflowCompleted_EmitsCorrectEvents()
    {
        SetupWorkflowStartSuccess("exec-1");
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await _queue.EnqueueAsync(request);
        _emittedEvents.Clear();

        _queue.NotifyWorkflowCompleted("exec-1", success: true);

        Assert.That(_emittedEvents, Has.Some.Matches<TaskQueueEvent>(
            e => e.EventType == TaskQueueEventType.IssueCompleted && e.IssueId == "issue1"));
    }

    #endregion

    #region Mixed Workflow and Direct Requests

    [Test]
    public async Task MixedQueue_WorkflowThenDirect_ProcessesCorrectly()
    {
        SetupWorkflowStartSuccess("exec-1");
        var workflowRequest = CreateRequest("issue1", workflowId: "wf-1");
        var directRequest = CreateRequest("issue2");

        await _queue.EnqueueAsync(workflowRequest);
        await _queue.EnqueueAsync(directRequest);

        // Workflow started for issue1
        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(It.IsAny<string>(), "wf-1", It.IsAny<TriggerContext>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Complete the workflow
        _queue.NotifyWorkflowCompleted("exec-1", success: true);

        // Direct agent start for issue2
        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(It.Is<AgentStartRequest>(r => r.IssueId == "issue2")),
            Times.Once);
    }

    [Test]
    public async Task ResumeAsync_WithWorkflowRequest_StartsWorkflow()
    {
        SetupWorkflowStartSuccess("exec-1");
        var request = CreateRequest("issue1", workflowId: "wf-1");

        _queue.Pause();
        await _queue.EnqueueAsync(request);

        // Not started yet (paused)
        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TriggerContext>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await _queue.ResumeAsync();

        // Now started
        _mockWorkflowService.Verify(
            w => w.StartWorkflowAsync(ProjectPath, "wf-1", It.IsAny<TriggerContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region No Workflow Service

    [Test]
    public async Task NoWorkflowService_WithWorkflowId_FallsBackToAgentStart()
    {
        // Queue without workflow service
        var queue = new TaskQueue(_mockAgentStartService.Object, _mockLogger.Object);
        var request = CreateRequest("issue1", workflowId: "wf-1");

        await queue.EnqueueAsync(request);

        // Falls back to agent start
        _mockAgentStartService.Verify(
            a => a.QueueAgentStartAsync(request),
            Times.Once);
    }

    #endregion
}
