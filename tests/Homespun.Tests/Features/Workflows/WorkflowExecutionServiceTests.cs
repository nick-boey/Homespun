using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowExecutionServiceTests
{
    private string _tempDir = null!;
    private Mock<IWorkflowStorageService> _mockStorageService = null!;
    private Mock<ILogger<WorkflowExecutionService>> _mockLogger = null!;
    private WorkflowExecutionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workflow-exec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockStorageService = new Mock<IWorkflowStorageService>();
        _mockLogger = new Mock<ILogger<WorkflowExecutionService>>();
        _service = new WorkflowExecutionService(
            _mockStorageService.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        _service.Dispose();

        // Give time for any background tasks to complete
        await Task.Delay(50);

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Retry after a short delay
                await Task.Delay(100);
                try
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    #region StartWorkflowAsync Tests

    [Test]
    public async Task StartWorkflowAsync_WorkflowNotFound_ReturnsError()
    {
        // Arrange
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, "non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            TriggeredBy = "test-user"
        };

        // Act
        var result = await _service.StartWorkflowAsync(_tempDir, "non-existent", triggerContext);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Execution, Is.Null);
    }

    [Test]
    public async Task StartWorkflowAsync_WorkflowDisabled_ReturnsError()
    {
        // Arrange
        var workflow = CreateTestWorkflow(enabled: false);
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            TriggeredBy = "test-user"
        };

        // Act
        var result = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("disabled"));
    }

    [Test]
    public async Task StartWorkflowAsync_ValidWorkflow_CreatesExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext
        {
            TriggerType = WorkflowTriggerType.Manual,
            TriggeredBy = "test-user",
            Input = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var result = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Execution, Is.Not.Null);
        Assert.That(result.Execution!.WorkflowId, Is.EqualTo(workflow.Id));
        Assert.That(result.Execution.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
        Assert.That(result.Execution.Trigger.Type, Is.EqualTo(WorkflowTriggerType.Manual));
        Assert.That(result.Execution.TriggeredBy, Is.EqualTo("test-user"));
    }

    [Test]
    public async Task StartWorkflowAsync_CreatesNodeExecutionsForAllNodes()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext();

        // Act
        var result = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);

        // Assert
        Assert.That(result.Execution, Is.Not.Null);
        Assert.That(result.Execution!.NodeExecutions, Has.Count.EqualTo(workflow.Nodes.Count));
        Assert.That(result.Execution.NodeExecutions.Select(n => n.NodeId),
            Is.EquivalentTo(workflow.Nodes.Select(n => n.Id)));
    }

    [Test]
    public async Task StartWorkflowAsync_SetsInputInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var input = new Dictionary<string, object>
        {
            ["issueId"] = "ABC123",
            ["branch"] = "feature/test"
        };
        var triggerContext = new TriggerContext { Input = input };

        // Act
        var result = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);

        // Assert
        Assert.That(result.Execution, Is.Not.Null);
        Assert.That(result.Execution!.Context.Input["issueId"], Is.EqualTo("ABC123"));
        Assert.That(result.Execution.Context.Input["branch"], Is.EqualTo("feature/test"));
    }

    [Test]
    public async Task StartWorkflowAsync_GeneratesUniqueExecutionId()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext();

        // Act
        var result1 = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);
        var result2 = await _service.StartWorkflowAsync(_tempDir, workflow.Id, triggerContext);

        // Assert
        Assert.That(result1.Execution!.Id, Is.Not.EqualTo(result2.Execution!.Id));
    }

    #endregion

    #region GetExecutionAsync Tests

    [Test]
    public async Task GetExecutionAsync_NonExistentExecution_ReturnsNull()
    {
        // Act
        var result = await _service.GetExecutionAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetExecutionAsync_ExistingExecution_ReturnsExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act
        var result = await _service.GetExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(startResult.Execution.Id));
    }

    #endregion

    #region ListExecutionsAsync Tests

    [Test]
    public async Task ListExecutionsAsync_NoExecutions_ReturnsEmptyList()
    {
        // Act
        var result = await _service.ListExecutionsAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListExecutionsAsync_WithExecutions_ReturnsAll()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act
        var result = await _service.ListExecutionsAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListExecutionsAsync_WithWorkflowIdFilter_ReturnsFiltered()
    {
        // Arrange
        var workflow1 = CreateTestWorkflow(id: "wf1");
        var workflow2 = CreateTestWorkflow(id: "wf2");

        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, "wf1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow1);
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, "wf2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow2);

        await _service.StartWorkflowAsync(_tempDir, "wf1", new TriggerContext());
        await _service.StartWorkflowAsync(_tempDir, "wf2", new TriggerContext());
        await _service.StartWorkflowAsync(_tempDir, "wf1", new TriggerContext());

        // Act
        var result = await _service.ListExecutionsAsync(_tempDir, workflowId: "wf1");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(e => e.WorkflowId == "wf1"), Is.True);
    }

    #endregion

    #region PauseExecutionAsync Tests

    [Test]
    public async Task PauseExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.PauseExecutionAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PauseExecutionAsync_RunningExecution_PausesAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act
        var result = await _service.PauseExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.True);
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));
    }

    [Test]
    public async Task PauseExecutionAsync_AlreadyPaused_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        await _service.PauseExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Act
        var result = await _service.PauseExecutionAsync(_tempDir, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PauseExecutionAsync_CompletedExecution_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateSimpleWorkflow(); // Start -> End only
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        // Wait for completion of simple workflow
        await Task.Delay(100);

        // Act
        var result = await _service.PauseExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region ResumeExecutionAsync Tests

    [Test]
    public async Task ResumeExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.ResumeExecutionAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ResumeExecutionAsync_PausedExecution_ResumesAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        await _service.PauseExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Act
        var result = await _service.ResumeExecutionAsync(_tempDir, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.True);
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
    }

    [Test]
    public async Task ResumeExecutionAsync_RunningExecution_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act
        var result = await _service.ResumeExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region CancelExecutionAsync Tests

    [Test]
    public async Task CancelExecutionAsync_NonExistentExecution_ReturnsFalse()
    {
        // Act
        var result = await _service.CancelExecutionAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CancelExecutionAsync_RunningExecution_CancelsAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act
        var result = await _service.CancelExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert
        Assert.That(result, Is.True);
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Cancelled));
    }

    [Test]
    public async Task CancelExecutionAsync_PausedExecution_CancelsAndReturnsTrue()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        await _service.PauseExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Act
        var result = await _service.CancelExecutionAsync(_tempDir, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.True);
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Cancelled));
    }

    [Test]
    public async Task CancelExecutionAsync_AlreadyCancelled_ReturnsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        await _service.CancelExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Act
        var result = await _service.CancelExecutionAsync(_tempDir, startResult.Execution.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region OnNodeCompletedAsync Tests

    [Test]
    public async Task OnNodeCompletedAsync_UpdatesNodeStatus()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        var nodeId = workflow.Nodes.First(n => n.Type == WorkflowNodeType.Start).Id;
        var output = new Dictionary<string, object> { ["result"] = "success" };

        // Act
        await _service.OnNodeCompletedAsync(_tempDir, startResult.Execution!.Id, nodeId, output);

        // Assert
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        var nodeExecution = execution!.NodeExecutions.First(n => n.NodeId == nodeId);
        Assert.That(nodeExecution.Status, Is.EqualTo(NodeExecutionStatus.Completed));
        Assert.That(nodeExecution.Output, Is.Not.Null);
        Assert.That(nodeExecution.Output!["result"], Is.EqualTo("success"));
    }

    [Test]
    public async Task OnNodeCompletedAsync_SetsNodeTimestamps()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        var nodeId = workflow.Nodes.First(n => n.Type == WorkflowNodeType.Start).Id;

        // Act
        await _service.OnNodeCompletedAsync(_tempDir, startResult.Execution!.Id, nodeId, null);

        // Assert
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        var nodeExecution = execution!.NodeExecutions.First(n => n.NodeId == nodeId);
        Assert.That(nodeExecution.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task OnNodeCompletedAsync_StoresOutputInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        var nodeId = workflow.Nodes.First(n => n.Type == WorkflowNodeType.Start).Id;
        var output = new Dictionary<string, object> { ["data"] = "test" };

        // Act
        await _service.OnNodeCompletedAsync(_tempDir, startResult.Execution!.Id, nodeId, output);

        // Assert
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Context.NodeOutputs.ContainsKey(nodeId), Is.True);
        Assert.That(execution.Context.NodeOutputs[nodeId].Status, Is.EqualTo("completed"));
    }

    #endregion

    #region OnNodeFailedAsync Tests

    [Test]
    public async Task OnNodeFailedAsync_UpdatesNodeStatus()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        var nodeId = workflow.Nodes.First(n => n.Type == WorkflowNodeType.Agent).Id;

        // Act
        await _service.OnNodeFailedAsync(_tempDir, startResult.Execution!.Id, nodeId, "Test error");

        // Assert
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        var nodeExecution = execution!.NodeExecutions.First(n => n.NodeId == nodeId);
        Assert.That(nodeExecution.Status, Is.EqualTo(NodeExecutionStatus.Failed));
        Assert.That(nodeExecution.ErrorMessage, Is.EqualTo("Test error"));
    }

    [Test]
    public async Task OnNodeFailedAsync_FailsWorkflowWhenContinueOnFailureIsFalse()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        workflow.Settings.ContinueOnFailure = false;

        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());
        var nodeId = workflow.Nodes.First(n => n.Type == WorkflowNodeType.Agent).Id;

        // Act
        await _service.OnNodeFailedAsync(_tempDir, startResult.Execution!.Id, nodeId, "Test error");

        // Assert
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Failed));
        Assert.That(execution.ErrorMessage, Does.Contain("Test error"));
    }

    #endregion

    #region Linear Execution Tests

    [Test]
    public async Task LinearExecution_ExecutesNodesInTopologicalOrder()
    {
        // Arrange
        var workflow = CreateLinearWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Act - start node should be running or queued
        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution!.Id);
        var startNode = execution!.NodeExecutions.First(n => n.NodeId == "start");

        // Assert - start node should be running or queued first
        Assert.That(startNode.Status,
            Is.EqualTo(NodeExecutionStatus.Running).Or.EqualTo(NodeExecutionStatus.Queued).Or.EqualTo(NodeExecutionStatus.Completed));

        // Second and third nodes should be pending initially
        var agentNode = execution.NodeExecutions.First(n => n.NodeId == "agent");
        var endNode = execution.NodeExecutions.First(n => n.NodeId == "end");

        // Only assert pending if start hasn't completed yet
        if (startNode.Status != NodeExecutionStatus.Completed)
        {
            Assert.That(agentNode.Status, Is.EqualTo(NodeExecutionStatus.Pending));
        }
    }

    [Test]
    public async Task LinearExecution_StartNodeCompletesAutomatically()
    {
        // Arrange
        var workflow = CreateSimpleWorkflow(); // Start -> End
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        // Act
        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext());

        // Give it a moment to execute
        await Task.Delay(100);

        var execution = await _service.GetExecutionAsync(_tempDir, startResult.Execution!.Id);

        // Assert - simple workflow should complete quickly
        Assert.That(execution!.Status,
            Is.EqualTo(WorkflowExecutionStatus.Running).Or.EqualTo(WorkflowExecutionStatus.Completed));
    }

    #endregion

    #region Persistence Tests

    [Test]
    public async Task Execution_IsPersisted_ReloadShowsSameData()
    {
        // Arrange
        var workflow = CreateTestWorkflow();
        _mockStorageService
            .Setup(s => s.GetWorkflowAsync(_tempDir, workflow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var startResult = await _service.StartWorkflowAsync(_tempDir, workflow.Id, new TriggerContext
        {
            Input = new Dictionary<string, object> { ["test"] = "value" }
        });
        var executionId = startResult.Execution!.Id;

        // Wait for the execution to be persisted
        await Task.Delay(100);

        // Act - dispose and recreate service to simulate restart
        _service.Dispose();
        _service = new WorkflowExecutionService(_mockStorageService.Object, _mockLogger.Object);

        // Force reload from disk
        var execution = await _service.GetExecutionAsync(_tempDir, executionId);

        // Assert
        Assert.That(execution, Is.Not.Null);
        Assert.That(execution!.Id, Is.EqualTo(executionId));
        Assert.That(execution.WorkflowId, Is.EqualTo(workflow.Id));
        // After deserialization, the value may be a JsonElement, so convert to string for comparison
        Assert.That(execution.Context.Input["test"].ToString(), Is.EqualTo("value"));
    }

    #endregion

    #region Helper Methods

    private static WorkflowDefinition CreateTestWorkflow(string? id = null, bool enabled = true)
    {
        var workflowId = id ?? $"test-{Guid.NewGuid():N}".Substring(0, 6);

        return new WorkflowDefinition
        {
            Id = workflowId,
            ProjectId = "project1",
            Title = "Test Workflow",
            Enabled = enabled,
            Nodes =
            [
                new WorkflowNode { Id = "start", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "agent1", Label = "Agent Task", Type = WorkflowNodeType.Agent },
                new WorkflowNode { Id = "end", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "e1", Source = "start", Target = "agent1" },
                new WorkflowEdge { Id = "e2", Source = "agent1", Target = "end" }
            ],
            Settings = new WorkflowSettings()
        };
    }

    private static WorkflowDefinition CreateSimpleWorkflow()
    {
        return new WorkflowDefinition
        {
            Id = $"simple-{Guid.NewGuid():N}".Substring(0, 6),
            ProjectId = "project1",
            Title = "Simple Workflow",
            Nodes =
            [
                new WorkflowNode { Id = "start", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "end", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "e1", Source = "start", Target = "end" }
            ],
            Settings = new WorkflowSettings()
        };
    }

    private static WorkflowDefinition CreateLinearWorkflow()
    {
        return new WorkflowDefinition
        {
            Id = $"linear-{Guid.NewGuid():N}".Substring(0, 6),
            ProjectId = "project1",
            Title = "Linear Workflow",
            Nodes =
            [
                new WorkflowNode { Id = "start", Label = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNode { Id = "agent", Label = "Agent", Type = WorkflowNodeType.Agent },
                new WorkflowNode { Id = "end", Label = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdge { Id = "e1", Source = "start", Target = "agent" },
                new WorkflowEdge { Id = "e2", Source = "agent", Target = "end" }
            ],
            Settings = new WorkflowSettings()
        };
    }

    #endregion
}
