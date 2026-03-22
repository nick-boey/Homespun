using System.Text.Json;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowContextStoreTests
{
    private string _tempDir = null!;
    private Mock<ILogger<WorkflowContextStore>> _mockLogger = null!;
    private WorkflowContextStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"context-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockLogger = new Mock<ILogger<WorkflowContextStore>>();
        _store = new WorkflowContextStore(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _store.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region InitializeContextAsync Tests

    [Test]
    public async Task InitializeContextAsync_CreatesContextWithExecutionInfo()
    {
        // Arrange
        var executionId = "exec123";
        var workflowId = "wf456";
        var workingDirectory = "/path/to/project";
        var triggerData = JsonSerializer.SerializeToElement(new { branch = "main", commit = "abc123" });

        // Act
        var context = await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            workflowId,
            workingDirectory,
            triggerData);

        // Assert
        Assert.That(context, Is.Not.Null);
        Assert.That(context.ExecutionId, Is.EqualTo(executionId));
        Assert.That(context.WorkflowId, Is.EqualTo(workflowId));
        Assert.That(context.WorkingDirectory, Is.EqualTo(workingDirectory));
        Assert.That(context.TriggerData.GetProperty("branch").GetString(), Is.EqualTo("main"));
    }

    [Test]
    public async Task InitializeContextAsync_PersistsContextToDisk()
    {
        // Arrange
        var executionId = "exec123";

        // Act
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Assert
        var contextFile = Path.Combine(_tempDir, ".workflows", $"context_{executionId}.json");
        Assert.That(File.Exists(contextFile), Is.True);
    }

    [Test]
    public async Task InitializeContextAsync_CreatesWorkflowsDirectoryIfNotExists()
    {
        // Arrange
        var executionId = "exec123";
        var workflowsDir = Path.Combine(_tempDir, ".workflows");

        // Ensure directory doesn't exist
        if (Directory.Exists(workflowsDir))
        {
            Directory.Delete(workflowsDir, recursive: true);
        }

        // Act
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Assert
        Assert.That(Directory.Exists(workflowsDir), Is.True);
    }

    #endregion

    #region GetContextAsync Tests

    [Test]
    public async Task GetContextAsync_NonExistentExecution_ReturnsNull()
    {
        // Act
        var result = await _store.GetContextAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetContextAsync_ExistingContext_ReturnsContext()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { test = "value" }));

        // Act
        var result = await _store.GetContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ExecutionId, Is.EqualTo(executionId));
    }

    [Test]
    public async Task GetContextAsync_ReturnsFromCache()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act - retrieve multiple times
        var result1 = await _store.GetContextAsync(_tempDir, executionId);
        var result2 = await _store.GetContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);
        Assert.That(result1!.ExecutionId, Is.EqualTo(result2!.ExecutionId));
    }

    [Test]
    public async Task GetContextAsync_LoadsFromDiskAfterRestart()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { test = "value" }));

        // Create new store instance
        _store.Dispose();
        _store = new WorkflowContextStore(_mockLogger.Object);

        // Act
        var result = await _store.GetContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ExecutionId, Is.EqualTo(executionId));
    }

    #endregion

    #region SetValueAsync Tests

    [Test]
    public async Task SetValueAsync_NonExistentContext_ReturnsFalse()
    {
        // Act
        var result = await _store.SetValueAsync(
            _tempDir,
            "non-existent",
            "key",
            JsonSerializer.SerializeToElement("value"));

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetValueAsync_AddsVariable()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        var result = await _store.SetValueAsync(
            _tempDir,
            executionId,
            "myVar",
            JsonSerializer.SerializeToElement("myValue"));

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Variables.ContainsKey("myVar"), Is.True);
        Assert.That(context.Variables["myVar"].GetString(), Is.EqualTo("myValue"));
    }

    [Test]
    public async Task SetValueAsync_UpdatesExistingVariable()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        await _store.SetValueAsync(
            _tempDir,
            executionId,
            "myVar",
            JsonSerializer.SerializeToElement("original"));

        // Act
        await _store.SetValueAsync(
            _tempDir,
            executionId,
            "myVar",
            JsonSerializer.SerializeToElement("updated"));

        // Assert
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Variables["myVar"].GetString(), Is.EqualTo("updated"));
    }

    [Test]
    public async Task SetValueAsync_PersistsToDisk()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        await _store.SetValueAsync(
            _tempDir,
            executionId,
            "persistedVar",
            JsonSerializer.SerializeToElement("persistedValue"));

        // Verify by reading file directly
        var contextFile = Path.Combine(_tempDir, ".workflows", $"context_{executionId}.json");
        var content = await File.ReadAllTextAsync(contextFile);

        // Assert
        Assert.That(content, Does.Contain("persistedVar"));
        Assert.That(content, Does.Contain("persistedValue"));
    }

    [Test]
    public async Task SetValueAsync_SupportsComplexValues()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var complexValue = JsonSerializer.SerializeToElement(new
        {
            nested = new { value = 42, list = new[] { 1, 2, 3 } }
        });

        // Act
        await _store.SetValueAsync(_tempDir, executionId, "complex", complexValue);

        // Assert
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Variables["complex"].GetProperty("nested").GetProperty("value").GetInt32(), Is.EqualTo(42));
    }

    #endregion

    #region GetValueAsync Tests

    [Test]
    public async Task GetValueAsync_NonExistentContext_ReturnsNull()
    {
        // Act
        var result = await _store.GetValueAsync(_tempDir, "non-existent", "key");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetValueAsync_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        var result = await _store.GetValueAsync(_tempDir, executionId, "nonExistentKey");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetValueAsync_ExistingKey_ReturnsValue()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        await _store.SetValueAsync(
            _tempDir,
            executionId,
            "myKey",
            JsonSerializer.SerializeToElement("myValue"));

        // Act
        var result = await _store.GetValueAsync(_tempDir, executionId, "myKey");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.GetString(), Is.EqualTo("myValue"));
    }

    #endregion

    #region MergeNodeOutputAsync Tests

    [Test]
    public async Task MergeNodeOutputAsync_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var output = new NodeOutput { Status = "completed" };

        // Act
        var result = await _store.MergeNodeOutputAsync(_tempDir, "non-existent", "node1", output);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MergeNodeOutputAsync_AddsNodeOutput()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var output = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { { "result", "success" } }
        };

        // Act
        var result = await _store.MergeNodeOutputAsync(_tempDir, executionId, "node1", output);

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.NodeOutputs.ContainsKey("node1"), Is.True);
        Assert.That(context.NodeOutputs["node1"].Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task MergeNodeOutputAsync_UpdatesExistingNodeOutput()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var originalOutput = new NodeOutput { Status = "running" };
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node1", originalOutput);

        var updatedOutput = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { { "finalResult", 42 } }
        };

        // Act
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node1", updatedOutput);

        // Assert
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.NodeOutputs["node1"].Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task MergeNodeOutputAsync_PersistsToDisk()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var output = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { { "persistedResult", "yes" } }
        };

        // Act
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node1", output);

        // Verify by reading file directly
        var contextFile = Path.Combine(_tempDir, ".workflows", $"context_{executionId}.json");
        var content = await File.ReadAllTextAsync(contextFile);

        // Assert
        Assert.That(content, Does.Contain("node1"));
        Assert.That(content, Does.Contain("completed"));
    }

    [Test]
    public async Task MergeNodeOutputAsync_SupportsMultipleNodes()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node1", new NodeOutput { Status = "completed" });
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node2", new NodeOutput { Status = "completed" });
        await _store.MergeNodeOutputAsync(_tempDir, executionId, "node3", new NodeOutput { Status = "failed", Error = "timeout" });

        // Assert
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.NodeOutputs.Count, Is.EqualTo(3));
        Assert.That(context.NodeOutputs["node1"].Status, Is.EqualTo("completed"));
        Assert.That(context.NodeOutputs["node2"].Status, Is.EqualTo("completed"));
        Assert.That(context.NodeOutputs["node3"].Status, Is.EqualTo("failed"));
        Assert.That(context.NodeOutputs["node3"].Error, Is.EqualTo("timeout"));
    }

    #endregion

    #region AddArtifactAsync Tests

    [Test]
    public async Task AddArtifactAsync_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var artifact = new WorkflowArtifact
        {
            Name = "test.txt",
            Path = "/path/to/test.txt",
            Type = "file"
        };

        // Act
        var result = await _store.AddArtifactAsync(_tempDir, "non-existent", artifact);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AddArtifactAsync_AddsArtifact()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var artifact = new WorkflowArtifact
        {
            Name = "output.log",
            Path = "/path/to/output.log",
            Type = "log"
        };

        // Act
        var result = await _store.AddArtifactAsync(_tempDir, executionId, artifact);

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Artifacts, Has.Count.EqualTo(1));
        Assert.That(context.Artifacts[0].Name, Is.EqualTo("output.log"));
    }

    [Test]
    public async Task AddArtifactAsync_SupportsMultipleArtifacts()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        await _store.AddArtifactAsync(_tempDir, executionId, new WorkflowArtifact { Name = "a.txt", Path = "/a.txt", Type = "file" });
        await _store.AddArtifactAsync(_tempDir, executionId, new WorkflowArtifact { Name = "b.txt", Path = "/b.txt", Type = "file" });

        // Assert
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Artifacts, Has.Count.EqualTo(2));
    }

    #endregion

    #region DeleteContextAsync Tests

    [Test]
    public async Task DeleteContextAsync_NonExistentContext_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteContextAsync(_tempDir, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteContextAsync_ExistingContext_ReturnsTrue()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        var result = await _store.DeleteContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteContextAsync_RemovesFromCache()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        await _store.DeleteContextAsync(_tempDir, executionId);
        var result = await _store.GetContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DeleteContextAsync_RemovesFromDisk()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        var contextFile = Path.Combine(_tempDir, ".workflows", $"context_{executionId}.json");
        Assert.That(File.Exists(contextFile), Is.True);

        // Act
        await _store.DeleteContextAsync(_tempDir, executionId);

        // Assert
        Assert.That(File.Exists(contextFile), Is.False);
    }

    #endregion

    #region Concurrency Tests

    [Test]
    public async Task SetValueAsync_ConcurrentWrites_AllSucceed()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act - perform concurrent writes
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _store.SetValueAsync(
                _tempDir,
                executionId,
                $"key{i}",
                JsonSerializer.SerializeToElement($"value{i}")))
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - all values should be present
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.Variables.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task MergeNodeOutputAsync_ConcurrentWrites_AllSucceed()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act - perform concurrent node output writes
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _store.MergeNodeOutputAsync(
                _tempDir,
                executionId,
                $"node{i}",
                new NodeOutput { Status = "completed" }))
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - all node outputs should be present
        var context = await _store.GetContextAsync(_tempDir, executionId);
        Assert.That(context!.NodeOutputs.Count, Is.EqualTo(10));
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task InitializeContextAsync_WithEmptyTriggerData_SetsEmptyObject()
    {
        // Arrange
        var emptyTriggerData = JsonSerializer.SerializeToElement(new { });

        // Act
        var context = await _store.InitializeContextAsync(
            _tempDir,
            "exec123",
            "wf456",
            "/path/to/project",
            emptyTriggerData);

        // Assert
        Assert.That(context.TriggerData.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task SetValueAsync_WithEmptyKey_ReturnsFalse()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        var result = await _store.SetValueAsync(
            _tempDir,
            executionId,
            "",
            JsonSerializer.SerializeToElement("value"));

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MergeNodeOutputAsync_WithEmptyNodeId_ReturnsFalse()
    {
        // Arrange
        var executionId = "exec123";
        await _store.InitializeContextAsync(
            _tempDir,
            executionId,
            "wf456",
            "/path/to/project",
            JsonSerializer.SerializeToElement(new { }));

        // Act
        var result = await _store.MergeNodeOutputAsync(
            _tempDir,
            executionId,
            "",
            new NodeOutput { Status = "completed" });

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
