using Homespun.Features.Workflows.Hubs;
using Homespun.Shared.Models.Workflows;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.Workflows;

/// <summary>
/// Unit tests for WorkflowHub extension methods.
/// </summary>
[TestFixture]
public class WorkflowHubExtensionsTests
{
    private Mock<IHubContext<WorkflowHub>> _hubContextMock = null!;
    private Mock<IHubClients> _clientsMock = null!;
    private Mock<IClientProxy> _allClientsMock = null!;
    private Mock<IClientProxy> _groupClientsMock = null!;

    [SetUp]
    public void SetUp()
    {
        _hubContextMock = new Mock<IHubContext<WorkflowHub>>();
        _clientsMock = new Mock<IHubClients>();
        _allClientsMock = new Mock<IClientProxy>();
        _groupClientsMock = new Mock<IClientProxy>();

        _hubContextMock.Setup(x => x.Clients).Returns(_clientsMock.Object);
        _clientsMock.Setup(x => x.All).Returns(_allClientsMock.Object);
        _clientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(_groupClientsMock.Object);
    }

    [Test]
    public async Task BroadcastStepStarted_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var stepId = "step-1";
        var stepIndex = 0;

        await _hubContextMock.Object.BroadcastStepStarted(executionId, stepId, stepIndex);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("StepStarted",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == stepId &&
                    (int)args[2]! == stepIndex),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastStepCompleted_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var stepId = "step-1";
        var status = StepExecutionStatus.Completed;
        var output = new Dictionary<string, object> { ["result"] = "success" };

        await _hubContextMock.Object.BroadcastStepCompleted(executionId, stepId, status, output);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("StepCompleted",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == stepId &&
                    (StepExecutionStatus)args[2]! == status),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastStepFailed_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var stepId = "step-1";
        var error = "Something went wrong";

        await _hubContextMock.Object.BroadcastStepFailed(executionId, stepId, error);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("StepFailed",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == stepId &&
                    (string)args[2]! == error),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastStepRetrying_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var stepId = "step-1";
        var retryCount = 2;
        var maxRetries = 3;

        await _hubContextMock.Object.BroadcastStepRetrying(executionId, stepId, retryCount, maxRetries);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("StepRetrying",
                It.Is<object?[]>(args =>
                    args.Length == 4 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == stepId &&
                    (int)args[2]! == retryCount &&
                    (int)args[3]! == maxRetries),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastWorkflowCompleted_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var status = WorkflowExecutionStatus.Completed;

        await _hubContextMock.Object.BroadcastWorkflowCompleted(executionId, status);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("WorkflowCompleted",
                It.Is<object?[]>(args =>
                    args.Length == 2 &&
                    (string)args[0]! == executionId &&
                    (WorkflowExecutionStatus)args[1]! == status),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastWorkflowFailed_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var error = "Workflow failed";

        await _hubContextMock.Object.BroadcastWorkflowFailed(executionId, error);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("WorkflowFailed",
                It.Is<object?[]>(args =>
                    args.Length == 2 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == error),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastGatePending_SendsToExecutionGroup()
    {
        var executionId = "exec-123";
        var stepId = "gate-1";
        var gateName = "Approval Gate";

        await _hubContextMock.Object.BroadcastGatePending(executionId, stepId, gateName);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _groupClientsMock.Verify(
            x => x.SendCoreAsync("GatePending",
                It.Is<object?[]>(args =>
                    args.Length == 3 &&
                    (string)args[0]! == executionId &&
                    (string)args[1]! == stepId &&
                    (string)args[2]! == gateName),
                default),
            Times.Once);
    }

    [Test]
    public async Task BroadcastStepStarted_AlsoSendsToProjectGroup_WhenProjectIdProvided()
    {
        var executionId = "exec-123";
        var stepId = "step-1";
        var stepIndex = 0;
        var projectId = "project-456";

        await _hubContextMock.Object.BroadcastStepStarted(executionId, stepId, stepIndex, projectId);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _clientsMock.Verify(x => x.Group($"project-{projectId}"), Times.Once);
    }

    [Test]
    public async Task BroadcastWorkflowCompleted_AlsoSendsToProjectGroup_WhenProjectIdProvided()
    {
        var executionId = "exec-123";
        var status = WorkflowExecutionStatus.Completed;
        var projectId = "project-456";

        await _hubContextMock.Object.BroadcastWorkflowCompleted(executionId, status, projectId);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _clientsMock.Verify(x => x.Group($"project-{projectId}"), Times.Once);
    }

    [Test]
    public async Task BroadcastWorkflowFailed_AlsoSendsToProjectGroup_WhenProjectIdProvided()
    {
        var executionId = "exec-123";
        var error = "Workflow failed";
        var projectId = "project-456";

        await _hubContextMock.Object.BroadcastWorkflowFailed(executionId, error, projectId);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _clientsMock.Verify(x => x.Group($"project-{projectId}"), Times.Once);
    }

    [Test]
    public async Task BroadcastStepStarted_DoesNotSendToProjectGroup_WhenProjectIdIsNull()
    {
        var executionId = "exec-123";

        await _hubContextMock.Object.BroadcastStepStarted(executionId, "step-1", 0);

        _clientsMock.Verify(x => x.Group($"execution-{executionId}"), Times.Once);
        _clientsMock.Verify(x => x.Group(It.Is<string>(s => s.StartsWith("project-"))), Times.Never);
    }
}
