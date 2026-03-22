using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowStorageServiceTests
{
    private string _tempDir = null!;
    private Mock<ILogger<WorkflowStorageService>> _mockLogger = null!;
    private WorkflowStorageService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<WorkflowStorageService>>();
        _service = new WorkflowStorageService(_mockLogger.Object);
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

    #region GetWorkflowAsync Tests

    [Test]
    public async Task GetWorkflowAsync_NonExistentWorkflow_ReturnsNull()
    {
        // Act
        var result = await _service.GetWorkflowAsync(_tempDir, "non-existent-id");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetWorkflowAsync_ExistingWorkflow_ReturnsWorkflow()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        var result = await _service.GetWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(workflow.Id));
        Assert.That(result.Title, Is.EqualTo("Test Workflow"));
    }

    [Test]
    public async Task GetWorkflowAsync_ReturnsFromCache()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Cached Workflow"
        });

        // Act - retrieve multiple times, should use cache
        var result1 = await _service.GetWorkflowAsync(_tempDir, workflow.Id);
        var result2 = await _service.GetWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);
        Assert.That(result1!.Id, Is.EqualTo(result2!.Id));
    }

    #endregion

    #region ListWorkflowsAsync Tests

    [Test]
    public async Task ListWorkflowsAsync_EmptyProject_ReturnsEmptyList()
    {
        // Act
        var result = await _service.ListWorkflowsAsync(_tempDir);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListWorkflowsAsync_WithWorkflows_ReturnsAllWorkflows()
    {
        // Arrange
        await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Workflow 1"
        });
        await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Workflow 2"
        });

        // Act
        var result = await _service.ListWorkflowsAsync(_tempDir);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(w => w.Title), Is.EquivalentTo(new[] { "Workflow 1", "Workflow 2" }));
    }

    #endregion

    #region CreateWorkflowAsync Tests

    [Test]
    public async Task CreateWorkflowAsync_WithTitleAndProjectId_CreatesWorkflow()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Assert
        Assert.That(workflow, Is.Not.Null);
        Assert.That(workflow.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(workflow.Title, Is.EqualTo("Test Workflow"));
        Assert.That(workflow.ProjectId, Is.EqualTo("project1"));
    }

    [Test]
    public async Task CreateWorkflowAsync_GeneratesUniqueHashId()
    {
        // Act
        var workflow1 = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Workflow 1"
        });
        var workflow2 = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Workflow 2"
        });

        // Assert
        Assert.That(workflow1.Id, Is.Not.EqualTo(workflow2.Id));
        Assert.That(workflow1.Id, Has.Length.EqualTo(6)); // 6 character hash
        Assert.That(workflow2.Id, Has.Length.EqualTo(6));
    }

    [Test]
    public async Task CreateWorkflowAsync_WithDescription_SetsDescription()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow",
            Description = "This is a test description"
        });

        // Assert
        Assert.That(workflow.Description, Is.EqualTo("This is a test description"));
    }

    [Test]
    public async Task CreateWorkflowAsync_WithNodes_SetsNodes()
    {
        // Arrange
        var nodes = new List<WorkflowNode>
        {
            new() { Id = "node1", Label = "Start", Type = WorkflowNodeType.Start },
            new() { Id = "node2", Label = "Agent", Type = WorkflowNodeType.Agent }
        };

        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow",
            Nodes = nodes
        });

        // Assert
        Assert.That(workflow.Nodes, Has.Count.EqualTo(2));
        Assert.That(workflow.Nodes[0].Label, Is.EqualTo("Start"));
        Assert.That(workflow.Nodes[1].Label, Is.EqualTo("Agent"));
    }

    [Test]
    public async Task CreateWorkflowAsync_WithEdges_SetsEdges()
    {
        // Arrange
        var edges = new List<WorkflowEdge>
        {
            new() { Id = "edge1", Source = "node1", Target = "node2" }
        };

        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow",
            Edges = edges
        });

        // Assert
        Assert.That(workflow.Edges, Has.Count.EqualTo(1));
        Assert.That(workflow.Edges[0].Source, Is.EqualTo("node1"));
    }

    [Test]
    public async Task CreateWorkflowAsync_WithTrigger_SetsTrigger()
    {
        // Arrange
        var trigger = new WorkflowTrigger
        {
            Type = WorkflowTriggerType.Event,
            Enabled = true
        };

        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow",
            Trigger = trigger
        });

        // Assert
        Assert.That(workflow.Trigger, Is.Not.Null);
        Assert.That(workflow.Trigger!.Type, Is.EqualTo(WorkflowTriggerType.Event));
    }

    [Test]
    public async Task CreateWorkflowAsync_WithSettings_SetsSettings()
    {
        // Arrange
        var settings = new WorkflowSettings
        {
            MaxConcurrentNodes = 10,
            DefaultTimeoutSeconds = 7200
        };

        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow",
            Settings = settings
        });

        // Assert
        Assert.That(workflow.Settings.MaxConcurrentNodes, Is.EqualTo(10));
        Assert.That(workflow.Settings.DefaultTimeoutSeconds, Is.EqualTo(7200));
    }

    [Test]
    public async Task CreateWorkflowAsync_DefaultsEnabled_ToTrue()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Assert
        Assert.That(workflow.Enabled, Is.True);
    }

    [Test]
    public async Task CreateWorkflowAsync_SetsVersion_ToOne()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Assert
        Assert.That(workflow.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateWorkflowAsync_SetsCreatedAtAndUpdatedAt()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        var afterCreate = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.That(workflow.CreatedAt, Is.GreaterThan(beforeCreate));
        Assert.That(workflow.CreatedAt, Is.LessThan(afterCreate));
        Assert.That(workflow.UpdatedAt, Is.EqualTo(workflow.CreatedAt));
    }

    [Test]
    public async Task CreateWorkflowAsync_PersistsToDisk()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Persistent Workflow"
        });

        // Assert - verify file was created in .workflows directory
        var workflowsDir = Path.Combine(_tempDir, ".workflows");
        Assert.That(Directory.Exists(workflowsDir), Is.True);

        var files = Directory.GetFiles(workflowsDir, "workflows_*.jsonl");
        Assert.That(files, Has.Length.GreaterThan(0));

        // Verify content contains the workflow
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.That(content, Does.Contain(workflow.Id));
        Assert.That(content, Does.Contain("Persistent Workflow"));
    }

    [Test]
    public async Task CreateWorkflowAsync_WorkflowCanBeRetrieved()
    {
        // Arrange
        var created = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Retrievable Workflow"
        });

        // Act
        var retrieved = await _service.GetWorkflowAsync(_tempDir, created.Id);

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
        Assert.That(retrieved.Title, Is.EqualTo("Retrievable Workflow"));
    }

    #endregion

    #region UpdateWorkflowAsync Tests

    [Test]
    public async Task UpdateWorkflowAsync_NonExistentWorkflow_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateWorkflowAsync(_tempDir, "non-existent", new UpdateWorkflowParams());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesTitle()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Original Title"
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Updated Title"
        });

        // Assert
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesDescription()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Description = "New description"
        });

        // Assert
        Assert.That(updated!.Description, Is.EqualTo("New description"));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesNodes()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        var newNodes = new List<WorkflowNode>
        {
            new() { Id = "newNode", Label = "New Node", Type = WorkflowNodeType.Action }
        };

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Nodes = newNodes
        });

        // Assert
        Assert.That(updated!.Nodes, Has.Count.EqualTo(1));
        Assert.That(updated.Nodes[0].Label, Is.EqualTo("New Node"));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesEdges()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        var newEdges = new List<WorkflowEdge>
        {
            new() { Id = "newEdge", Source = "n1", Target = "n2" }
        };

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Edges = newEdges
        });

        // Assert
        Assert.That(updated!.Edges, Has.Count.EqualTo(1));
        Assert.That(updated.Edges[0].Source, Is.EqualTo("n1"));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesTrigger()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        var newTrigger = new WorkflowTrigger
        {
            Type = WorkflowTriggerType.Scheduled,
            Enabled = true
        };

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Trigger = newTrigger
        });

        // Assert
        Assert.That(updated!.Trigger, Is.Not.Null);
        Assert.That(updated.Trigger!.Type, Is.EqualTo(WorkflowTriggerType.Scheduled));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesSettings()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        var newSettings = new WorkflowSettings { MaxConcurrentNodes = 20 };

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Settings = newSettings
        });

        // Assert
        Assert.That(updated!.Settings.MaxConcurrentNodes, Is.EqualTo(20));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesEnabled()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Enabled = false
        });

        // Assert
        Assert.That(updated!.Enabled, Is.False);
    }

    [Test]
    public async Task UpdateWorkflowAsync_IncrementsVersion()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });
        Assert.That(workflow.Version, Is.EqualTo(1));

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Updated Title"
        });

        // Assert
        Assert.That(updated!.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesUpdatedAt()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });
        var originalUpdatedAt = workflow.UpdatedAt;

        // Wait a bit to ensure time difference
        await Task.Delay(10);

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Updated Title"
        });

        // Assert
        Assert.That(updated!.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    [Test]
    public async Task UpdateWorkflowAsync_PreservesCreatedAt()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });
        var originalCreatedAt = workflow.CreatedAt;

        // Act
        var updated = await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Updated Title"
        });

        // Assert
        Assert.That(updated!.CreatedAt, Is.EqualTo(originalCreatedAt));
    }

    [Test]
    public async Task UpdateWorkflowAsync_UpdatesCache()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Original Title"
        });

        // Act
        await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Updated Title"
        });

        var retrieved = await _service.GetWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(retrieved!.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task UpdateWorkflowAsync_PersistsChanges()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Original Title"
        });

        // Act
        await _service.UpdateWorkflowAsync(_tempDir, workflow.Id, new UpdateWorkflowParams
        {
            Title = "Persisted Update"
        });

        // Create a new service instance to verify persistence
        var newService = new WorkflowStorageService(_mockLogger.Object);
        var retrieved = await newService.GetWorkflowAsync(_tempDir, workflow.Id);
        newService.Dispose();

        // Assert
        Assert.That(retrieved!.Title, Is.EqualTo("Persisted Update"));
    }

    #endregion

    #region DeleteWorkflowAsync Tests

    [Test]
    public async Task DeleteWorkflowAsync_NonExistentWorkflow_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteWorkflowAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteWorkflowAsync_ExistingWorkflow_ReturnsTrue()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        var result = await _service.DeleteWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteWorkflowAsync_RemovesFromCache()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        await _service.DeleteWorkflowAsync(_tempDir, workflow.Id);
        var retrieved = await _service.GetWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task DeleteWorkflowAsync_RemovesFromList()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        await _service.DeleteWorkflowAsync(_tempDir, workflow.Id);
        var list = await _service.ListWorkflowsAsync(_tempDir);

        // Assert
        Assert.That(list, Is.Empty);
    }

    [Test]
    public async Task DeleteWorkflowAsync_PersistsRemoval()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Act
        await _service.DeleteWorkflowAsync(_tempDir, workflow.Id);

        // Create a new service instance to verify persistence
        var newService = new WorkflowStorageService(_mockLogger.Object);
        var retrieved = await newService.GetWorkflowAsync(_tempDir, workflow.Id);
        newService.Dispose();

        // Assert
        Assert.That(retrieved, Is.Null);
    }

    #endregion

    #region ReloadFromDiskAsync Tests

    [Test]
    public async Task ReloadFromDiskAsync_ReloadsWorkflowsFromDisk()
    {
        // Arrange
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Original Title"
        });

        // Modify the file on disk directly
        var workflowsDir = Path.Combine(_tempDir, ".workflows");
        var files = Directory.GetFiles(workflowsDir, "workflows_*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);
        var modifiedContent = content.Replace("Original Title", "Modified On Disk");
        await File.WriteAllTextAsync(files[0], modifiedContent);

        // Act
        await _service.ReloadFromDiskAsync(_tempDir);
        var retrieved = await _service.GetWorkflowAsync(_tempDir, workflow.Id);

        // Assert
        Assert.That(retrieved!.Title, Is.EqualTo("Modified On Disk"));
    }

    #endregion

    #region Schema Version Tests

    [Test]
    public async Task CreateWorkflowAsync_IncludesSchemaVersion()
    {
        // Act
        var workflow = await _service.CreateWorkflowAsync(_tempDir, new CreateWorkflowParams
        {
            ProjectId = "project1",
            Title = "Test Workflow"
        });

        // Assert - verify file contains schema version
        var workflowsDir = Path.Combine(_tempDir, ".workflows");
        var files = Directory.GetFiles(workflowsDir, "workflows_*.jsonl");
        var content = await File.ReadAllTextAsync(files[0]);

        // Schema version should be present in the stored data
        Assert.That(content, Does.Contain("schemaVersion"));
    }

    #endregion
}
