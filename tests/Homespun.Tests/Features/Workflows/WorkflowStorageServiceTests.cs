using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowStorageServiceTests
{
    private WorkflowStorageService _service = null!;
    private Mock<ILogger<WorkflowStorageService>> _mockLogger = null!;
    private string _testProjectPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<WorkflowStorageService>>();
        _service = new WorkflowStorageService(_mockLogger.Object);
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testProjectPath);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();

        if (Directory.Exists(_testProjectPath))
        {
            Directory.Delete(_testProjectPath, recursive: true);
        }
    }

    #region CreateWorkflowAsync Tests

    [Test]
    public async Task CreateWorkflowAsync_ValidParams_CreatesWorkflow()
    {
        // Arrange
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Test Workflow",
            Description = "A test workflow"
        };

        // Act
        var result = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Title, Is.EqualTo("Test Workflow"));
            Assert.That(result.Description, Is.EqualTo("A test workflow"));
            Assert.That(result.ProjectId, Is.EqualTo("project-1"));
            Assert.That(result.Version, Is.EqualTo(1));
            Assert.That(result.Enabled, Is.True);
        });
    }

    [Test]
    public async Task CreateWorkflowAsync_WithSteps_CreatesWorkflowWithSteps()
    {
        // Arrange
        var steps = new List<WorkflowStep>
        {
            new() { Id = "step-1", Name = "Agent Step", StepType = WorkflowStepType.Agent },
            new() { Id = "step-2", Name = "Gate Step", StepType = WorkflowStepType.Gate }
        };
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Workflow with Steps",
            Steps = steps
        };

        // Act
        var result = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Steps, Has.Count.EqualTo(2));
            Assert.That(result.Steps[0].Id, Is.EqualTo("step-1"));
            Assert.That(result.Steps[0].StepType, Is.EqualTo(WorkflowStepType.Agent));
            Assert.That(result.Steps[1].Id, Is.EqualTo("step-2"));
            Assert.That(result.Steps[1].StepType, Is.EqualTo(WorkflowStepType.Gate));
        });
    }

    [Test]
    public async Task CreateWorkflowAsync_PersistsToDisk()
    {
        // Arrange
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Persistent Workflow"
        };

        // Act
        var created = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Assert - Check the .workflows directory exists with a file
        var workflowsDir = Path.Combine(_testProjectPath, ".workflows");
        Assert.That(Directory.Exists(workflowsDir), Is.True);

        var files = Directory.GetFiles(workflowsDir, "workflows_*.jsonl");
        Assert.That(files, Has.Length.GreaterThan(0));
    }

    [Test]
    public async Task CreateWorkflowAsync_GeneratesUniqueId()
    {
        // Arrange
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Same Title"
        };

        // Act
        var first = await _service.CreateWorkflowAsync(_testProjectPath, createParams);
        var second = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Assert
        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    #endregion

    #region GetWorkflowAsync Tests

    [Test]
    public async Task GetWorkflowAsync_ExistingWorkflow_ReturnsWorkflow()
    {
        // Arrange
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Test Workflow"
        };
        var created = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Act
        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
            Assert.That(retrieved.Title, Is.EqualTo("Test Workflow"));
        });
    }

    [Test]
    public async Task GetWorkflowAsync_NonExistentWorkflow_ReturnsNull()
    {
        // Act
        var result = await _service.GetWorkflowAsync(_testProjectPath, "non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetWorkflowAsync_AfterReload_ReturnsWorkflow()
    {
        // Arrange
        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Reload Test Workflow"
        };
        var created = await _service.CreateWorkflowAsync(_testProjectPath, createParams);

        // Force reload from disk
        await _service.ReloadFromDiskAsync(_testProjectPath);

        // Act
        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
            Assert.That(retrieved.Title, Is.EqualTo("Reload Test Workflow"));
        });
    }

    #endregion

    #region ListWorkflowsAsync Tests

    [Test]
    public async Task ListWorkflowsAsync_EmptyProject_ReturnsEmptyList()
    {
        // Act
        var result = await _service.ListWorkflowsAsync(_testProjectPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListWorkflowsAsync_WithWorkflows_ReturnsAllWorkflows()
    {
        // Arrange
        await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Workflow 1"
        });
        await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Workflow 2"
        });
        await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Workflow 3"
        });

        // Act
        var result = await _service.ListWorkflowsAsync(_testProjectPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListWorkflowsAsync_AfterReload_ReturnsAllWorkflows()
    {
        // Arrange
        await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Persistent Workflow 1"
        });
        await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Persistent Workflow 2"
        });

        // Force reload from disk
        await _service.ReloadFromDiskAsync(_testProjectPath);

        // Act
        var result = await _service.ListWorkflowsAsync(_testProjectPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion

    #region UpdateWorkflowAsync Tests

    [Test]
    public async Task UpdateWorkflowAsync_ExistingWorkflow_UpdatesTitle()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Original Title"
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Title = "Updated Title" });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Title, Is.EqualTo("Updated Title"));
            Assert.That(updated.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateWorkflowAsync_ExistingWorkflow_UpdatesDescription()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Test Workflow",
            Description = "Original description"
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Description = "Updated description" });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Description, Is.EqualTo("Updated description"));
            Assert.That(updated.Title, Is.EqualTo("Test Workflow")); // Unchanged
        });
    }

    [Test]
    public async Task UpdateWorkflowAsync_ExistingWorkflow_UpdatesEnabled()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Test Workflow",
            Enabled = true
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Enabled = false });

        // Assert
        Assert.That(updated!.Enabled, Is.False);
    }

    [Test]
    public async Task UpdateWorkflowAsync_ExistingWorkflow_UpdatesSteps()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Test Workflow",
            Steps = [new WorkflowStep { Id = "step-1", Name = "Step 1", StepType = WorkflowStepType.Agent }]
        });

        var newSteps = new List<WorkflowStep>
        {
            new() { Id = "step-1", Name = "Step 1", StepType = WorkflowStepType.Agent },
            new() { Id = "step-2", Name = "Step 2", StepType = WorkflowStepType.ServerAction },
            new() { Id = "step-3", Name = "Step 3", StepType = WorkflowStepType.Gate }
        };

        // Act
        var updated = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Steps = newSteps });

        // Assert
        Assert.That(updated!.Steps, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task UpdateWorkflowAsync_NonExistentWorkflow_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            "non-existent-id",
            new UpdateWorkflowParams { Title = "New Title" });

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateWorkflowAsync_IncrementsVersion()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Version Test Workflow"
        });
        Assert.That(created.Version, Is.EqualTo(1));

        // Act - Update twice
        var firstUpdate = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Title = "Updated 1" });
        var secondUpdate = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Title = "Updated 2" });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstUpdate!.Version, Is.EqualTo(2));
            Assert.That(secondUpdate!.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task UpdateWorkflowAsync_PreservesOriginalCreatedAt()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Timestamp Test"
        });
        var originalCreatedAt = created.CreatedAt;

        // Act
        await Task.Delay(10); // Small delay to ensure time difference
        var updated = await _service.UpdateWorkflowAsync(
            _testProjectPath,
            created.Id,
            new UpdateWorkflowParams { Title = "Updated Title" });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(updated!.CreatedAt, Is.EqualTo(originalCreatedAt));
            Assert.That(updated.UpdatedAt, Is.GreaterThan(originalCreatedAt));
        });
    }

    #endregion

    #region DeleteWorkflowAsync Tests

    [Test]
    public async Task DeleteWorkflowAsync_ExistingWorkflow_DeletesAndReturnsTrue()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "To Be Deleted"
        });

        // Act
        var deleted = await _service.DeleteWorkflowAsync(_testProjectPath, created.Id);

        // Assert
        Assert.That(deleted, Is.True);

        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task DeleteWorkflowAsync_NonExistentWorkflow_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteWorkflowAsync(_testProjectPath, "non-existent-id");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteWorkflowAsync_RemovesFromDisk()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Persistent Delete Test"
        });

        // Act
        await _service.DeleteWorkflowAsync(_testProjectPath, created.Id);

        // Force reload and verify
        await _service.ReloadFromDiskAsync(_testProjectPath);
        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);

        // Assert
        Assert.That(retrieved, Is.Null);
    }

    #endregion

    #region ReloadFromDiskAsync Tests

    [Test]
    public async Task ReloadFromDiskAsync_ClearsCache()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Reload Test"
        });

        // Act
        await _service.ReloadFromDiskAsync(_testProjectPath);
        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);

        // Assert - Should still find workflow from disk
        Assert.That(retrieved, Is.Not.Null);
    }

    [Test]
    public async Task ReloadFromDiskAsync_EmptyDirectory_DoesNotFail()
    {
        // Act & Assert - Should not throw
        await _service.ReloadFromDiskAsync(_testProjectPath);
        var result = await _service.ListWorkflowsAsync(_testProjectPath);
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region Serialization Round-Trip Tests

    [Test]
    public async Task SerializationRoundTrip_WorkflowWithSteps_PreservesAllFields()
    {
        // Arrange
        var steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step-1",
                Name = "Agent Step",
                StepType = WorkflowStepType.Agent,
                Prompt = "Do something {{input.task}}",
                SessionMode = Homespun.Shared.Models.Sessions.SessionMode.Plan,
                OnSuccess = new StepTransition { Type = StepTransitionType.NextStep },
                OnFailure = new StepTransition { Type = StepTransitionType.GoToStep, TargetStepId = "step-3" },
                MaxRetries = 2,
                RetryDelaySeconds = 60,
                Condition = "input.shouldRun == true"
            },
            new()
            {
                Id = "step-2",
                Name = "Server Action",
                StepType = WorkflowStepType.ServerAction,
                OnSuccess = new StepTransition { Type = StepTransitionType.NextStep },
                OnFailure = new StepTransition { Type = StepTransitionType.Exit }
            },
            new()
            {
                Id = "step-3",
                Name = "Gate Step",
                StepType = WorkflowStepType.Gate,
                OnSuccess = new StepTransition { Type = StepTransitionType.Exit },
                OnFailure = new StepTransition { Type = StepTransitionType.Retry }
            }
        };

        var createParams = new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Round Trip Test",
            Description = "Testing serialization",
            Steps = steps,
            Settings = new WorkflowSettings { DefaultTimeoutSeconds = 7200, ContinueOnFailure = true }
        };

        // Act
        var created = await _service.CreateWorkflowAsync(_testProjectPath, createParams);
        await _service.ReloadFromDiskAsync(_testProjectPath);
        var retrieved = await _service.GetWorkflowAsync(_testProjectPath, created.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(retrieved!.Steps, Has.Count.EqualTo(3));
            Assert.That(retrieved.Settings.DefaultTimeoutSeconds, Is.EqualTo(7200));
            Assert.That(retrieved.Settings.ContinueOnFailure, Is.True);

            var agentStep = retrieved.Steps[0];
            Assert.That(agentStep.Id, Is.EqualTo("step-1"));
            Assert.That(agentStep.StepType, Is.EqualTo(WorkflowStepType.Agent));
            Assert.That(agentStep.Prompt, Is.EqualTo("Do something {{input.task}}"));
            Assert.That(agentStep.SessionMode, Is.EqualTo(Homespun.Shared.Models.Sessions.SessionMode.Plan));
            Assert.That(agentStep.OnFailure.Type, Is.EqualTo(StepTransitionType.GoToStep));
            Assert.That(agentStep.OnFailure.TargetStepId, Is.EqualTo("step-3"));
            Assert.That(agentStep.MaxRetries, Is.EqualTo(2));
            Assert.That(agentStep.Condition, Is.EqualTo("input.shouldRun == true"));

            var gateStep = retrieved.Steps[2];
            Assert.That(gateStep.StepType, Is.EqualTo(WorkflowStepType.Gate));
            Assert.That(gateStep.OnFailure.Type, Is.EqualTo(StepTransitionType.Retry));
        });
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task ConcurrentCreation_CreatesAllWorkflows()
    {
        // Arrange
        var tasks = Enumerable.Range(1, 10).Select(i =>
            _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
            {
                ProjectId = "project-1",
                Title = $"Concurrent Workflow {i}"
            }));

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        var allWorkflows = await _service.ListWorkflowsAsync(_testProjectPath);
        Assert.That(allWorkflows, Has.Count.EqualTo(10));
        Assert.That(results.Select(r => r.Id).Distinct().Count(), Is.EqualTo(10));
    }

    [Test]
    public async Task ConcurrentReadWrite_MaintainsConsistency()
    {
        // Arrange - Create initial workflow
        var created = await _service.CreateWorkflowAsync(_testProjectPath, new CreateWorkflowParams
        {
            ProjectId = "project-1",
            Title = "Concurrent Test"
        });

        // Act - Concurrent reads and updates
        var tasks = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            tasks.Add(_service.GetWorkflowAsync(_testProjectPath, created.Id));
            tasks.Add(_service.UpdateWorkflowAsync(_testProjectPath, created.Id,
                new UpdateWorkflowParams { Description = $"Update {i}" }));
        }

        await Task.WhenAll(tasks);

        // Assert - Final state should be consistent
        var final = await _service.GetWorkflowAsync(_testProjectPath, created.Id);
        Assert.That(final, Is.Not.Null);
        Assert.That(final!.Version, Is.GreaterThanOrEqualTo(1));
    }

    #endregion
}
