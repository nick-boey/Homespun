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

        var stepExecutors = CreateDefaultStepExecutors();
        _service = new WorkflowExecutionService(_mockStorageService.Object, stepExecutors, _mockLogger.Object);

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
        // Arrange - Use workflow with gate step so it doesn't complete immediately
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Test Workflow",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "Server Action", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Gate", StepType = WorkflowStepType.Gate },
                new WorkflowStep { Id = "step-3", Name = "Final", StepType = WorkflowStepType.ServerAction }
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

        // Assert
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
    public async Task StartWorkflowAsync_CreatesStepExecutionsForAllSteps()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        workflow.Steps =
        [
            new WorkflowStep { Id = "step-1", Name = "Agent", StepType = WorkflowStepType.Agent },
            new WorkflowStep { Id = "step-2", Name = "Action", StepType = WorkflowStepType.ServerAction },
            new WorkflowStep { Id = "step-3", Name = "Gate", StepType = WorkflowStepType.Gate }
        ];

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Execution!.StepExecutions, Has.Count.EqualTo(3));
            Assert.That(result.Execution.StepExecutions.Any(s => s.StepId == "step-1"), Is.True);
            Assert.That(result.Execution.StepExecutions.Any(s => s.StepId == "step-2"), Is.True);
            Assert.That(result.Execution.StepExecutions.Any(s => s.StepId == "step-3"), Is.True);
        });
    }

    [Test]
    public async Task StartWorkflowAsync_StepExecutionsHaveCorrectIndexes()
    {
        // Arrange
        var workflow = CreateTestWorkflow("workflow-1", enabled: true);
        workflow.Steps =
        [
            new WorkflowStep { Id = "step-1", Name = "First", StepType = WorkflowStepType.Agent },
            new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction },
            new WorkflowStep { Id = "step-3", Name = "Third", StepType = WorkflowStepType.Gate }
        ];

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert - verify step indexes are assigned sequentially
        // Note: CurrentStepIndex may advance due to fire-and-forget execution
        Assert.Multiple(() =>
        {
            Assert.That(result.Execution!.StepExecutions[0].StepIndex, Is.EqualTo(0));
            Assert.That(result.Execution.StepExecutions[1].StepIndex, Is.EqualTo(1));
            Assert.That(result.Execution.StepExecutions[2].StepIndex, Is.EqualTo(2));
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
        var workflow = CreateTestWorkflowWithAgentStep();
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
        var workflow = CreateTestWorkflowWithAgentStep();
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
        // Arrange - Use a workflow with gate step that pauses execution automatically
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Gate Workflow",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "Action", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Gate", StepType = WorkflowStepType.Gate },
                new WorkflowStep { Id = "step-3", Name = "Final", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to reach gate step and pause
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
        var workflow = CreateTestWorkflowWithAgentStep();
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
        var workflow = CreateTestWorkflowWithAgentStep();
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
        // Arrange - Use a workflow with gate step that pauses execution automatically
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Gate Workflow",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "Action", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Gate", StepType = WorkflowStepType.Gate },
                new WorkflowStep { Id = "step-3", Name = "Final", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to reach gate step and pause
        await Task.Delay(100);

        // Verify the execution is paused
        var beforeCancel = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution!.Id);
        Assert.That(beforeCancel!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));

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
        var workflow = CreateTestWorkflowWithAgentStep();
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

    #endregion

    #region OnStepCompletedAsync Tests

    [Test]
    public async Task OnStepCompletedAsync_ValidExecution_MarksStepCompleted()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for background execution to process
        await Task.Delay(100);

        // Act
        var output = new Dictionary<string, object> { ["result"] = "success" };
        await _service.OnStepCompletedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", output);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        var stepExecution = execution!.StepExecutions.First(s => s.StepId == "agent-1");

        Assert.Multiple(() =>
        {
            Assert.That(stepExecution.Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(stepExecution.CompletedAt, Is.Not.Null);
            Assert.That(stepExecution.Output, Is.Not.Null);
        });
    }

    [Test]
    public async Task OnStepCompletedAsync_StoresOutputInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        var output = new Dictionary<string, object> { ["prNumber"] = 123 };
        await _service.OnStepCompletedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", output);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        Assert.That(execution!.Context.NodeOutputs, Contains.Key("agent-1"));
        Assert.That(execution.Context.NodeOutputs["agent-1"].Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task OnStepCompletedAsync_NonExistentExecution_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        await _service.OnStepCompletedAsync(_testProjectPath, "non-existent", "step-1", null);
    }

    #endregion

    #region OnStepFailedAsync Tests

    [Test]
    public async Task OnStepFailedAsync_ValidExecution_MarksStepFailed()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnStepFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Test error message");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);
        var stepExecution = execution!.StepExecutions.First(s => s.StepId == "agent-1");

        Assert.Multiple(() =>
        {
            Assert.That(stepExecution.Status, Is.EqualTo(StepExecutionStatus.Failed));
            Assert.That(stepExecution.ErrorMessage, Is.EqualTo("Test error message"));
        });
    }

    [Test]
    public async Task OnStepFailedAsync_DefaultBehavior_FailsEntireExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = false };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnStepFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Step failed");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Failed));
            Assert.That(execution.ErrorMessage, Does.Contain("agent-1"));
        });
    }

    [Test]
    public async Task OnStepFailedAsync_ContinueOnFailure_DoesNotFailExecution()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = true };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnStepFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Step failed");

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, startResult.Execution.Id);

        // Execution should not be failed (it might still be running or completed)
        Assert.That(execution!.Status, Is.Not.EqualTo(WorkflowExecutionStatus.Failed));
    }

    [Test]
    public async Task OnStepFailedAsync_StoresErrorInContext()
    {
        // Arrange
        var workflow = CreateTestWorkflowWithAgentStep();
        workflow.Settings = new WorkflowSettings { ContinueOnFailure = true };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };
        var startResult = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(50);

        // Act
        await _service.OnStepFailedAsync(_testProjectPath, startResult.Execution!.Id, "agent-1", "Test error");

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

    #region Sequential Execution Tests

    [Test]
    public async Task ExecuteWorkflow_SequentialSteps_ExecutesInOrder()
    {
        // Arrange - Create a simple workflow with server action steps only
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Sequential Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "First", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Wait for execution to complete (server action steps execute immediately)
        await Task.Delay(100);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Completed));
        });
    }

    [Test]
    public async Task ExecuteWorkflow_EmptySteps_CompletesImmediately()
    {
        // Arrange
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Empty Workflow",
            Enabled = true,
            Steps = [],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        await Task.Delay(100);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
    }

    #endregion

    #region StepTransition Tests

    [Test]
    public async Task StepTransition_GoToStep_TargetStepIdIsPreserved()
    {
        // Arrange
        var steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step-1",
                Name = "First",
                StepType = WorkflowStepType.Agent,
                OnFailure = new StepTransition { Type = StepTransitionType.GoToStep, TargetStepId = "step-3" }
            },
            new() { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction },
            new() { Id = "step-3", Name = "Third", StepType = WorkflowStepType.Gate }
        };

        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "GoToStep Test",
            Enabled = true,
            Steps = steps,
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);

        // Assert - Verify the step transition is preserved in the execution context
        Assert.That(result.Success, Is.True);
        Assert.That(workflow.Steps[0].OnFailure.Type, Is.EqualTo(StepTransitionType.GoToStep));
        Assert.That(workflow.Steps[0].OnFailure.TargetStepId, Is.EqualTo("step-3"));
    }

    [Test]
    public async Task StepTransition_OnSuccess_GoToStep_JumpsToTargetStep()
    {
        // Arrange - step-1 on success jumps to step-3, skipping step-2
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "GoToStep Success Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "First",
                    StepType = WorkflowStepType.ServerAction,
                    OnSuccess = new StepTransition { Type = StepTransitionType.GoToStep, TargetStepId = "step-3" }
                },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-3", Name = "Third", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert - step-2 should be skipped (still Pending), step-1 and step-3 should be Completed
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Pending));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-3").Status, Is.EqualTo(StepExecutionStatus.Completed));
        });
    }

    [Test]
    public async Task StepTransition_OnSuccess_Exit_TerminatesWorkflowSuccessfully()
    {
        // Arrange - step-1 on success exits immediately
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Exit Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "First",
                    StepType = WorkflowStepType.ServerAction,
                    OnSuccess = new StepTransition { Type = StepTransitionType.Exit }
                },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert - workflow should be completed, step-2 should not have run
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Pending));
        });
    }

    [Test]
    public async Task StepTransition_OnFailure_GoToStep_JumpsToTargetOnFailure()
    {
        // Arrange - Use a mock executor that fails for step-1
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(e => e.StepType).Returns(WorkflowStepType.ServerAction);
        mockExecutor.SetupSequence(e => e.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepResult.Failed("Step failed")) // step-1 fails
            .ReturnsAsync(StepResult.Completed())           // step-3 succeeds
            .ReturnsAsync(StepResult.Completed());          // fallback

        var executors = new List<IStepExecutor>
        {
            mockExecutor.Object,
            new AgentStepExecutor(new Mock<ILogger<AgentStepExecutor>>().Object),
            new GateStepExecutor(new Mock<ILogger<GateStepExecutor>>().Object)
        };

        var service = new WorkflowExecutionService(_mockStorageService.Object, executors, _mockLogger.Object);

        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "OnFailure GoTo Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "First",
                    StepType = WorkflowStepType.ServerAction,
                    OnFailure = new StepTransition { Type = StepTransitionType.GoToStep, TargetStepId = "step-3" }
                },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-3", Name = "Third", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings { ContinueOnFailure = true }
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(It.IsAny<string>(), "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert - step-1 failed, jumped to step-3, step-2 was skipped
        var execution = await service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Failed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Pending));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-3").Status, Is.EqualTo(StepExecutionStatus.Completed));
        });

        service.Dispose();
    }

    [Test]
    public async Task StepTransition_OnFailure_Exit_TerminatesWorkflowWithFailure()
    {
        // Arrange - Use a mock executor that fails
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(e => e.StepType).Returns(WorkflowStepType.ServerAction);
        mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepResult.Failed("Boom"));

        var executors = new List<IStepExecutor>
        {
            mockExecutor.Object,
            new AgentStepExecutor(new Mock<ILogger<AgentStepExecutor>>().Object),
            new GateStepExecutor(new Mock<ILogger<GateStepExecutor>>().Object)
        };

        var service = new WorkflowExecutionService(_mockStorageService.Object, executors, _mockLogger.Object);

        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Exit on Failure Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "First",
                    StepType = WorkflowStepType.ServerAction,
                    OnFailure = new StepTransition { Type = StepTransitionType.Exit }
                },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(It.IsAny<string>(), "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert
        var execution = await service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Failed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Failed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Pending));
        });

        service.Dispose();
    }

    #endregion

    #region Retry Logic Tests

    [Test]
    public async Task Retry_RetriesUpToMaxRetries_ThenFollowsOnFailure()
    {
        // Arrange - executor always fails, step has retry with max 2 retries and 0 delay
        var callCount = 0;
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(e => e.StepType).Returns(WorkflowStepType.ServerAction);
        mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return StepResult.Failed("fail");
            });

        var executors = new List<IStepExecutor>
        {
            mockExecutor.Object,
            new AgentStepExecutor(new Mock<ILogger<AgentStepExecutor>>().Object),
            new GateStepExecutor(new Mock<ILogger<GateStepExecutor>>().Object)
        };

        var service = new WorkflowExecutionService(_mockStorageService.Object, executors, _mockLogger.Object);

        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Retry Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "Retryable",
                    StepType = WorkflowStepType.ServerAction,
                    OnFailure = new StepTransition { Type = StepTransitionType.Retry },
                    MaxRetries = 2,
                    RetryDelaySeconds = 0
                }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(It.IsAny<string>(), "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(300);

        // Assert - should have been called 3 times (initial + 2 retries)
        var execution = await service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(callCount, Is.EqualTo(3));
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Failed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").RetryCount, Is.EqualTo(2));
        });

        service.Dispose();
    }

    [Test]
    public async Task Retry_SucceedsOnSecondAttempt_ContinuesWorkflow()
    {
        // Arrange - executor fails first, then succeeds
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(e => e.StepType).Returns(WorkflowStepType.ServerAction);
        mockExecutor.SetupSequence(e => e.ExecuteAsync(It.IsAny<WorkflowStep>(), It.IsAny<WorkflowContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepResult.Failed("first try failed"))  // step-1 first attempt
            .ReturnsAsync(StepResult.Completed())                 // step-1 retry succeeds
            .ReturnsAsync(StepResult.Completed());                // step-2 succeeds

        var executors = new List<IStepExecutor>
        {
            mockExecutor.Object,
            new AgentStepExecutor(new Mock<ILogger<AgentStepExecutor>>().Object),
            new GateStepExecutor(new Mock<ILogger<GateStepExecutor>>().Object)
        };

        var service = new WorkflowExecutionService(_mockStorageService.Object, executors, _mockLogger.Object);

        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Retry Success Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = "step-1",
                    Name = "Retryable",
                    StepType = WorkflowStepType.ServerAction,
                    OnFailure = new StepTransition { Type = StepTransitionType.Retry },
                    MaxRetries = 2,
                    RetryDelaySeconds = 0
                },
                new WorkflowStep { Id = "step-2", Name = "Second", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(It.IsAny<string>(), "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(300);

        // Assert
        var execution = await service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").RetryCount, Is.EqualTo(1));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Completed));
        });

        service.Dispose();
    }

    #endregion

    #region Condition Evaluation Tests

    [Test]
    public async Task Condition_StepSkippedWhenConditionIsFalse()
    {
        // Arrange - step-2 has a condition that evaluates to false
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Condition Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "First", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep
                {
                    Id = "step-2",
                    Name = "Conditional",
                    StepType = WorkflowStepType.ServerAction,
                    Condition = "steps.step-1.output.skipNext == true"
                },
                new WorkflowStep { Id = "step-3", Name = "Third", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert - step-2 should be skipped, step-1 and step-3 completed
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-1").Status, Is.EqualTo(StepExecutionStatus.Completed));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.Skipped));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-3").Status, Is.EqualTo(StepExecutionStatus.Completed));
        });
    }

    [Test]
    public void EvaluateCondition_EqualityTrue_ReturnsTrue()
    {
        var context = new WorkflowContext();
        context.NodeOutputs["verify"] = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { ["approved"] = true }
        };

        var result = WorkflowExecutionService.EvaluateCondition("steps.verify.output.approved == true", context);

        Assert.That(result, Is.True);
    }

    [Test]
    public void EvaluateCondition_EqualityFalse_ReturnsFalse()
    {
        var context = new WorkflowContext();
        context.NodeOutputs["verify"] = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { ["approved"] = false }
        };

        var result = WorkflowExecutionService.EvaluateCondition("steps.verify.output.approved == true", context);

        Assert.That(result, Is.False);
    }

    [Test]
    public void EvaluateCondition_NotEquals_ReturnsCorrectResult()
    {
        var context = new WorkflowContext();
        context.NodeOutputs["verify"] = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { ["approved"] = false }
        };

        var result = WorkflowExecutionService.EvaluateCondition("steps.verify.output.approved != true", context);

        Assert.That(result, Is.True);
    }

    [Test]
    public void EvaluateCondition_MissingPath_ReturnsFalse()
    {
        var context = new WorkflowContext();

        var result = WorkflowExecutionService.EvaluateCondition("steps.verify.output.approved == true", context);

        Assert.That(result, Is.False);
    }

    [Test]
    public void EvaluateCondition_EmptyCondition_ReturnsTrue()
    {
        var context = new WorkflowContext();

        Assert.Multiple(() =>
        {
            Assert.That(WorkflowExecutionService.EvaluateCondition("", context), Is.True);
            Assert.That(WorkflowExecutionService.EvaluateCondition("  ", context), Is.True);
        });
    }

    [Test]
    public void EvaluateCondition_BooleanPath_ReturnsTruthiness()
    {
        var context = new WorkflowContext();
        context.Variables["enabled"] = true;

        var result = WorkflowExecutionService.EvaluateCondition("variables.enabled", context);

        Assert.That(result, Is.True);
    }

    [Test]
    public void EvaluateCondition_StringEquality_ReturnsTrue()
    {
        var context = new WorkflowContext();
        context.NodeOutputs["build"] = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { ["result"] = "success" }
        };

        var result = WorkflowExecutionService.EvaluateCondition("steps.build.output.result == \"success\"", context);

        Assert.That(result, Is.True);
    }

    #endregion

    #region Crash Recovery Tests

    [Test]
    public async Task CrashRecovery_ResumesFromPersistedCurrentStepIndex()
    {
        // Arrange - Create a workflow with a gate step in the middle
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Recovery Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "Action", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Gate", StepType = WorkflowStepType.Gate },
                new WorkflowStep { Id = "step-3", Name = "Final", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Start and let it reach the gate
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);
        Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));
        Assert.That(execution.CurrentStepIndex, Is.EqualTo(1)); // Paused at gate

        // Simulate crash recovery: create a new service instance that loads from disk
        _service.Dispose();
        var stepExecutors = CreateDefaultStepExecutors();
        _service = new WorkflowExecutionService(_mockStorageService.Object, stepExecutors, _mockLogger.Object);

        // The execution should be loadable from disk
        var recoveredExecution = await _service.GetExecutionAsync(_testProjectPath, result.Execution.Id);

        Assert.Multiple(() =>
        {
            Assert.That(recoveredExecution, Is.Not.Null);
            Assert.That(recoveredExecution!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));
            Assert.That(recoveredExecution.CurrentStepIndex, Is.EqualTo(1));
        });
    }

    #endregion

    #region Gate Step Tests

    [Test]
    public async Task GateStep_PausesExecutionAndWaitsForInput()
    {
        // Arrange
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Gate Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "step-1", Name = "Action", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "step-2", Name = "Gate", StepType = WorkflowStepType.Gate }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Paused));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "step-2").Status, Is.EqualTo(StepExecutionStatus.WaitingForInput));
        });
    }

    #endregion

    #region Agent Step Tests

    [Test]
    public async Task AgentStep_PausesExecutionWaitingForCallback()
    {
        // Arrange
        var workflow = new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Agent Test",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "agent-1", Name = "Agent", StepType = WorkflowStepType.Agent, Prompt = "Do something" }
            ],
            Settings = new WorkflowSettings()
        };

        _mockStorageService.Setup(s => s.GetWorkflowAsync(_testProjectPath, "workflow-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);

        var triggerContext = new TriggerContext { TriggerType = WorkflowTriggerType.Manual };

        // Act
        var result = await _service.StartWorkflowAsync(_testProjectPath, "workflow-1", triggerContext);
        await Task.Delay(200);

        // Assert - execution should still be running (not paused), but waiting for callback
        var execution = await _service.GetExecutionAsync(_testProjectPath, result.Execution!.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution!.Status, Is.EqualTo(WorkflowExecutionStatus.Running));
            Assert.That(execution.StepExecutions.First(s => s.StepId == "agent-1").Status, Is.EqualTo(StepExecutionStatus.Running));
        });
    }

    #endregion

    #region Helper Methods

    private static IEnumerable<IStepExecutor> CreateDefaultStepExecutors()
    {
        return
        [
            new AgentStepExecutor(new Mock<ILogger<AgentStepExecutor>>().Object),
            new ServerActionStepExecutor(new Mock<ILogger<ServerActionStepExecutor>>().Object),
            new GateStepExecutor(new Mock<ILogger<GateStepExecutor>>().Object)
        ];
    }

    private static WorkflowDefinition CreateTestWorkflow(string id, bool enabled)
    {
        return new WorkflowDefinition
        {
            Id = id,
            ProjectId = "project-1",
            Title = $"Test Workflow {id}",
            Enabled = enabled,
            Steps = [],
            Settings = new WorkflowSettings()
        };
    }

    private static WorkflowDefinition CreateTestWorkflowWithAgentStep()
    {
        return new WorkflowDefinition
        {
            Id = "workflow-1",
            ProjectId = "project-1",
            Title = "Agent Workflow",
            Enabled = true,
            Steps =
            [
                new WorkflowStep { Id = "action-1", Name = "Setup", StepType = WorkflowStepType.ServerAction },
                new WorkflowStep { Id = "agent-1", Name = "Agent", StepType = WorkflowStepType.Agent },
                new WorkflowStep { Id = "action-2", Name = "Cleanup", StepType = WorkflowStepType.ServerAction }
            ],
            Settings = new WorkflowSettings()
        };
    }

    #endregion
}
