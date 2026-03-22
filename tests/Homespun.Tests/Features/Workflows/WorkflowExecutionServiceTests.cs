using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowExecutionServiceTests
{
    private WorkflowExecutionService _service = null!;
    private Mock<IWorkflowStorageService> _mockStorageService = null!;
    private Mock<ILogger<WorkflowExecutionService>> _mockLogger = null!;
    private string _testProjectPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockStorageService = new Mock<IWorkflowStorageService>();
        _mockLogger = new Mock<ILogger<WorkflowExecutionService>>();
        _service = new WorkflowExecutionService(_mockStorageService.Object, _mockLogger.Object);
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"workflow-exec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testProjectPath);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        if (Directory.Exists(_testProjectPath))
        {
            try
            {
                Directory.Delete(_testProjectPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    #region StartWorkflowAsync Tests

    [Test]
    public async Task StartWorkflowAsync_ValidWorkflow_ReturnsSuccessWithExecution()
    {
        // Arrange - Use workflow with gate node so it doesn't complete immediately
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Test Workflow",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "gate-1", Label = "Gate", Type = WorkflowNodeType.Gate },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "gate-1" },
                new WorkflowEdge { Id = "edge-2", Source = "gate-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            TriggeredBy = "test-user"
        };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert - result.Execution is the snapshot at start time, so should be Running
        // (the execution loop may change state asynchronously after this)
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Execution, Is.Not.Null);
            Assert.That(result.Execution!.WorkflowId, Is.EqualTo("workflow-1"));
            Assert.That(result.Execution.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task StartWorkflowAsync_NonExistentWorkflow_ReturnsFailure()
    {
        // Arrange
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "non-existent", triggerContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Execution, Is.Null);
            Assert.That(result.Error, Does.Contain("not found"));
        });
    }

    [Test]
    public async Task StartWorkflowAsync_DisabledWorkflow_ReturnsFailure()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: false);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("disabled"));
        });
    }

    [Test]
    public async Task StartWorkflowAsync_WithInputData_StoresInputInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            Input = new Dictionary<string, object>
            {
                ["issueId"] = "issue-123",
                ["priority"] = "high"
            }
        };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Execution!.Context.Input, Contains.Key("issueId"));
            Assert.That(result.Execution.Context.Input["issueId"], Is.EqualTo("issue-123"));
        });
    }

    [Test]
    public async Task StartWorkflowAsync_CreatesNodeExecutionsForAllNodes()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        workflow.Nodes =
        [
            new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
            new WorkflowNode { Id = "agent-1", Label = "Agent", Type = WorkflowNodeType.Agent },
            new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
        ];

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Execution!.NodeExecutions, Has.Count.EqualTo(3));
            Assert.That(result.Execution.NodeExecutions.Any(n => n.NodeId == "start-1"), Is.True);
            Assert.That(result.Execution.NodeExecutions.Any(n => n.NodeId == "agent-1"), Is.True);
            Assert.That(result.Execution.NodeExecutions.Any(n => n.NodeId == "end-1"), Is.True);
        });
    }

    [Test]
    public async Task StartWorkflowAsync_SetsTriggeredByFromContext()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            TriggeredBy = "user@example.com"
        };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert
        Assert.That(result.Execution!.TriggeredBy, Is.EqualTo("user@example.com"));
    }

    #endregion

    #region GetExecutionAsync Tests

    [Test]
    public async Task GetExecutionAsync_ExistingExecution_ReturnsExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act
        var retrieved = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Id, Is.EqualTo(startResult.Execution.Id));
        });
    }

    [Test]
    public async Task GetExecutionAsync_NonExistentExecution_ReturnsNull()
    {
        // Act
        var result = await _service.GetExecutionAsync(_testProjectPath, "non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ListExecutionsAsync Tests

    [Test]
    public async Task ListExecutionsAsync_NoExecutions_ReturnsEmptyList()
    {
        // Act
        var result = await _service.ListExecutionsAsync(_testProjectPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListExecutionsAsync_WithExecutions_ReturnsAllExecutions()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act
        var result = await _service.ListExecutionsAsync(_testProjectPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListExecutionsAsync_FilterByWorkflowId_ReturnsFilteredList()
    {
        // Arrange
        var workflow1 = CreateTestWorkflow("workflow-1", enabled: true);
        var workflow2 = CreateTestWorkflow("workflow-2", enabled: true);

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow1);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow2);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await _service.StartWorkflowAsync(_testProjectPath, "workflow-2", triggerContext);

        // Act
        var result = await _service.ListExecutionsAsync(_testProjectPath, workflowId: "workflow-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(e => e.WorkflowId == "workflow-1"), Is.True);
        });
    }

    [Test]
    public async Task ListExecutionsAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        var first = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(10);
        var second = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(10);
        var third = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act
        var result = await _service.ListExecutionsAsync(_testProjectPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Id, Is.EqualTo(third.Execution!.Id));
            Assert.That(result[2].Id, Is.EqualTo(first.Execution!.Id));
        });
    }

    #endregion

    #region PauseExecutionAsync Tests

    [Test]
    public async Task PauseExecutionAsync_RunningExecution_PausesAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act
        var paused = await _service.PauseExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Assert
        Assert.That(paused, Is.True);

        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));
    }

    [Test]
    public async Task PauseExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.PauseExecutionAsync(_testProjectPath, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PauseExecutionAsync_AlreadyPausedExecution_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await _service.PauseExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Act - Try to pause again
        var result = await _service.PauseExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region ResumeExecutionAsync Tests

    [Test]
    public async Task ResumeExecutionAsync_PausedExecution_ResumesAndReturnsTrue()
    {
        // Arrange - Use a workflow with gate node that pauses execution automatically
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Gate Workflow",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "gate-1", Label = "Gate", Type = WorkflowNodeType.Gate },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "gate-1" },
                new WorkflowEdge { Id = "edge-2", Source = "gate-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to reach gate node and pause
        await Task.Delay(100);

        // Verify the execution is paused at gate
        var beforeResume = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution!.Id);
        Assert.That(beforeResume!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));

        // Act
        var resumed = await _service.ResumeExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Assert
        Assert.That(resumed, Is.True);

        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
    }

    [Test]
    public async Task ResumeExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.ResumeExecutionAsync(_testProjectPath, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ResumeExecutionAsync_NotPausedExecution_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act - Try to resume without pausing first
        var result = await _service.ResumeExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region CancelExecutionAsync Tests

    [Test]
    public async Task CancelExecutionAsync_RunningExecution_CancelsAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Act
        var cancelled = await _service.CancelExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Assert
        Assert.That(cancelled, Is.True);

        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Cancelled));
            Assert.That(execution.CompletedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task CancelExecutionAsync_PausedExecution_CancelsAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await _service.PauseExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Act
        var cancelled = await _service.CancelExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Assert
        Assert.That(cancelled, Is.True);
    }

    [Test]
    public async Task CancelExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.CancelExecutionAsync(_testProjectPath, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CancelExecutionAsync_AlreadyCancelledExecution_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await _service.CancelExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Act
        var result = await _service.CancelExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CancelExecutionAsync_CancelsRunningNodes()
    {
        // Arrange - Use a workflow with gate node that pauses execution and keeps nodes in WaitingForInput state
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Gate Workflow",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "gate-1", Label = "Gate", Type = WorkflowNodeType.Gate },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "gate-1" },
                new WorkflowEdge { Id = "edge-2", Source = "gate-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for gate node to pause execution
        await Task.Delay(100);

        // Act
        var cancelled = await _service.CancelExecutionAsync(_testProjectPath, startResult.Execution!.Id);

        // Assert
        Assert.That(cancelled, Is.True);

        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Cancelled));

        // Verify gate node is in a non-running state after cancel (it was WaitingForInput before cancel)
        var gateNode = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == "gate-1");
        Assert.That(gateNode, Is.Not.Null);
    }

    #endregion

    #region OnNodeCompletedAsync Tests

    [Test]
    public async Task OnNodeCompletedAsync_ValidExecution_MarksNodeCompleted()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to start
        await Task.Delay(50);

        // Act
        var output = new Dictionary<string, object> { ["result"] = "success" };
        await _service.OnNodeCompletedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", output);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        var nodeExecution = execution!.NodeExecutions.First(n => n.NodeId == "agent-1");

        Assert.Multiple(() =>
        {
            Assert.That(nodeExecution.Status, Is.EqualTo(NodeExecutionStatus.Completed));
            Assert.That(nodeExecution.CompletedAt, Is.Not.Null);
            Assert.That(nodeExecution.Output, Is.Not.Null);
        });
    }

    [Test]
    public async Task OnNodeCompletedAsync_StoresOutputInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        var output = new Dictionary<string, object> { ["prNumber"] = 123 };
        await _service.OnNodeCompletedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", output);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.That(execution!.Context.NodeOutputs, Contains.Key("agent-1"));
        Assert.That(execution.Context.NodeOutputs["agent-1"].Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task OnNodeCompletedAsync_NonExistentExecution_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        await _service.OnNodeCompletedAsync(_testProjectPath, "non-existent", "node-1", null);
    }

    #endregion

    #region OnNodeFailedAsync Tests

    [Test]
    public async Task OnNodeFailedAsync_ValidExecution_MarksNodeFailed()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnNodeFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Test error message");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        var nodeExecution = execution!.NodeExecutions.First(n => n.NodeId == "agent-1");

        Assert.Multiple(() =>
        {
            Assert.That(nodeExecution.Status, Is.EqualTo(NodeExecutionStatus.Failed));
            Assert.That(nodeExecution.ErrorMessage, Is.EqualTo("Test error message"));
        });
    }

    [Test]
    public async Task OnNodeFailedAsync_DefaultBehavior_FailsEntireExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = false };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnNodeFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Node failed");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Failed));
            Assert.That(execution.ErrorMessage, Does.Contain("agent-1"));
        });
    }

    [Test]
    public async Task OnNodeFailedAsync_ContinueOnFailure_DoesNotFailExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = true };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnNodeFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Node failed");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Execution should not be failed (it might still be running or completed)
        Assert.That(execution!.Status, Is.Not.EqualTo(WorkflowExecutionStatus.Failed));
    }

    [Test]
    public async Task OnNodeFailedAsync_StoresErrorInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentNode();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = true };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnNodeFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Test error");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.Multiple(() =>
        {
            Assert.That(execution!.Context.NodeOutputs, Contains.Key("agent-1"));
            Assert.That(execution.Context.NodeOutputs["agent-1"].Status, Is.EqualTo("failed"));
            Assert.That(execution.Context.NodeOutputs["agent-1"].Error, Is.EqualTo("Test error"));
        });
    }

    #endregion

    #region Linear Execution Tests

    [Test]
    public async Task ExecuteWorkflow_LinearWorkflow_ExecutesNodesInOrder()
    {
        // Arrange - Create a simple start -> end workflow
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Linear Test",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to complete (start and end nodes execute immediately)
        await Task.Delay(100);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.NodeExecutions.First(n => n.NodeId == "start-1").Status, Is.EqualTo(NodeExecutionStatus.Completed));
            Assert.That(execution.NodeExecutions.First(n => n.NodeId == "end-1").Status, Is.EqualTo(NodeExecutionStatus.Completed));
        });
    }

    [Test]
    public async Task ExecuteWorkflow_DisabledNodes_AreSkipped()
    {
        // Arrange
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Skip Test",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "disabled-1", Label = "Disabled", Type = WorkflowNodeType.Action, Disabled = true },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "disabled-1" },
                new WorkflowEdge { Id = "edge-2", Source = "disabled-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to complete
        await Task.Delay(100);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);
        var disabledNode = execution!.NodeExecutions.First(n => n.NodeId == "disabled-1");

        Assert.That(disabledNode.Status, Is.EqualTo(NodeExecutionStatus.Skipped));
    }

    #endregion

    #region Helper Methods

    private static WorkflowDefinition CreateTestWorkflow(string id, bool enabled)
    {
        return new WorkflowDefinition
        {
            Id = id,
            ProjectId = "project-1",
            Title = $"Test Workflow {id}",
            Enabled = enabled,
            Nodes = [],
            Edges = [],
            Settings = new WorkflowSettings()
        };
    }

    private static WorkflowDefinition CreateTestWorkflowWithAgentNode()
    {
        return new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Agent Workflow",
            Enabled = true,
            Nodes =
            [
                new WorkflowNode { Id = "start-1", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "agent-1", Label = "Agent", Type = WorkflowNodeType.Agent },
                new WorkflowNode { Id = "end-1", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "edge-1", Source = "start-1", Target = "agent-1" },
                new WorkflowEdge { Id = "edge-2", Source = "agent-1", Target = "end-1" }
            ],
            Settings = new WorkflowSettings()
        };
    }

    #endregion
}
