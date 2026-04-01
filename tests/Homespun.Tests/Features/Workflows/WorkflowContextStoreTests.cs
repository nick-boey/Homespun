using System.Text.Json;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Workflows;

[TestFixture]
public class WorkflowContextStoreTests
{
    private WorkflowContextStore _store = null!;
    private Mock<ILogger<WorkflowContextStore>> _mockLogger = null!;
    private string _testProjectPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<WorkflowContextStore>>();
        _store = new WorkflowContextStore(_mockLogger.Object);
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"context-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testProjectPath);
    }

    [TearDown]
    public void TearDown()
    {
        _store.Dispose();

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

    #region InitializeContextAsync Tests

    [Test]
    public async Task InitializeContextAsync_ValidParams_CreatesContext()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { eventType = "IssueCreated" });

        // Act
        var context = await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(context, Is.Not.Null);
            Assert.That(context.ExecutionId, Is.EqualTo("execution-1"));
            Assert.That(context.WorkflowId, Is.EqualTo("workflow-1"));
            Assert.That(context.WorkingDirectory, Is.EqualTo("/working/dir"));
            Assert.That(context.NodeOutputs, Is.Empty);
            Assert.That(context.Variables, Is.Empty);
            Assert.That(context.Artifacts, Is.Empty);
        });
    }

    [Test]
    public async Task InitializeContextAsync_PersistsToDisk()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { test = "data" });

        // Act
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Assert - Check the file was created
        var workflowsDir = Path.Combine(_testProjectPath, ".fleece", "workflows");
        var contextFile = Path.Combine(workflowsDir, "context_execution-1.json");
        Assert.That(File.Exists(contextFile), Is.True);
    }

    [Test]
    public async Task InitializeContextAsync_SetsTimestamps()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        var beforeInit = DateTime.UtcNow;

        // Act
        var context = await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var afterInit = DateTime.UtcNow;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(context.CreatedAt, Is.GreaterThanOrEqualTo(beforeInit));
            Assert.That(context.CreatedAt, Is.LessThanOrEqualTo(afterInit));
            // UpdatedAt should be very close to CreatedAt (within 1 second)
            Assert.That((context.UpdatedAt - context.CreatedAt).TotalSeconds, Is.LessThan(1));
        });
    }

    #endregion

    #region GetContextAsync Tests

    [Test]
    public async Task GetContextAsync_ExistingContext_ReturnsContext()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act
        var retrieved = await _store.GetContextAsync(_testProjectPath, "execution-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.ExecutionId, Is.EqualTo("execution-1"));
            Assert.That(retrieved.WorkflowId, Is.EqualTo("workflow-1"));
        });
    }

    [Test]
    public async Task GetContextAsync_NonExistentContext_ReturnsNull()
    {
        // Act
        var result = await _store.GetContextAsync(_testProjectPath, "non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetContextAsync_LoadsFromDiskWhenNotInCache()
    {
        // Arrange - Create context with first store instance
        var triggerData = JsonSerializer.SerializeToElement(new { issueId = "123" });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Dispose and create new store to clear cache
        _store.Dispose();
        _store = new WorkflowContextStore(_mockLogger.Object);

        // Act
        var retrieved = await _store.GetContextAsync(_testProjectPath, "execution-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.ExecutionId, Is.EqualTo("execution-1"));
        });
    }

    [Test]
    public async Task GetContextAsync_CachesContext()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act - Get twice, second should be from cache
        var first = await _store.GetContextAsync(_testProjectPath, "execution-1");
        var second = await _store.GetContextAsync(_testProjectPath, "execution-1");

        // Assert - Should return same reference
        Assert.That(ReferenceEquals(first, second), Is.True);
    }

    #endregion

    #region SetValueAsync Tests

    [Test]
    public async Task SetValueAsync_ValidKey_SetsValueAndReturnsTrue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var value = JsonSerializer.SerializeToElement("test-value");

        // Act
        var result = await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", value);

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.Variables, Contains.Key("myVar"));
    }

    [Test]
    public async Task SetValueAsync_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var value = JsonSerializer.SerializeToElement("test-value");

        // Act
        var result = await _store.SetValueAsync(_testProjectPath, "non-existent", "myVar", value);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetValueAsync_EmptyKey_ReturnsFalse()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var value = JsonSerializer.SerializeToElement("test-value");

        // Act
        var result = await _store.SetValueAsync(_testProjectPath, "execution-1", "", value);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetValueAsync_UpdatesExistingValue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var firstValue = JsonSerializer.SerializeToElement("first");
        var secondValue = JsonSerializer.SerializeToElement("second");

        await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", firstValue);

        // Act
        await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", secondValue);

        // Assert
        var retrieved = await _store.GetValueAsync(_testProjectPath, "execution-1", "myVar");
        Assert.That(retrieved!.Value.GetString(), Is.EqualTo("second"));
    }

    [Test]
    public async Task SetValueAsync_UpdatesTimestamp()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        var context = await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);
        var originalUpdatedAt = context.UpdatedAt;

        await Task.Delay(10);

        // Act
        var value = JsonSerializer.SerializeToElement("test");
        await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", value);

        // Assert
        var updated = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(updated!.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    [Test]
    public async Task SetValueAsync_PersistsToDisk()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var value = JsonSerializer.SerializeToElement("persisted-value");
        await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", value);

        // Create new store to clear cache
        _store.Dispose();
        _store = new WorkflowContextStore(_mockLogger.Object);

        // Act
        var retrieved = await _store.GetValueAsync(_testProjectPath, "execution-1", "myVar");

        // Assert
        Assert.That(retrieved!.Value.GetString(), Is.EqualTo("persisted-value"));
    }

    #endregion

    #region GetValueAsync Tests

    [Test]
    public async Task GetValueAsync_ExistingKey_ReturnsValue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var value = JsonSerializer.SerializeToElement(42);
        await _store.SetValueAsync(_testProjectPath, "execution-1", "count", value);

        // Act
        var retrieved = await _store.GetValueAsync(_testProjectPath, "execution-1", "count");

        // Assert
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Value.GetInt32(), Is.EqualTo(42));
    }

    [Test]
    public async Task GetValueAsync_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act
        var result = await _store.GetValueAsync(_testProjectPath, "execution-1", "nonexistent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetValueAsync_NonExistentContext_ReturnsNull()
    {
        // Act
        var result = await _store.GetValueAsync(_testProjectPath, "non-existent", "myVar");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetValueAsync_ComplexValue_ReturnsCorrectly()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var complexValue = JsonSerializer.SerializeToElement(new
        {
            name = "test",
            numbers = new[] { 1, 2, 3 },
            nested = new { value = true }
        });
        await _store.SetValueAsync(_testProjectPath, "execution-1", "complex", complexValue);

        // Act
        var retrieved = await _store.GetValueAsync(_testProjectPath, "execution-1", "complex");

        // Assert
        Assert.That(retrieved!.Value.GetProperty("name").GetString(), Is.EqualTo("test"));
        Assert.That(retrieved.Value.GetProperty("nested").GetProperty("value").GetBoolean(), Is.True);
    }

    #endregion

    #region MergeNodeOutputAsync Tests

    [Test]
    public async Task MergeNodeOutputAsync_ValidNodeOutput_MergesAndReturnsTrue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var output = new NodeOutput
        {
            Status = "completed",
            Data = new Dictionary<string, object> { ["result"] = "success" }
        };

        // Act
        var result = await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1", output);

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.Multiple(() =>
        {
            Assert.That(context!.NodeOutputs, Contains.Key("node-1"));
            Assert.That(context.NodeOutputs["node-1"].Status, Is.EqualTo("completed"));
        });
    }

    [Test]
    public async Task MergeNodeOutputAsync_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var output = new NodeOutput { Status = "completed" };

        // Act
        var result = await _store.MergeNodeOutputAsync(_testProjectPath, "non-existent", "node-1", output);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MergeNodeOutputAsync_EmptyNodeId_ReturnsFalse()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var output = new NodeOutput { Status = "completed" };

        // Act
        var result = await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "", output);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task MergeNodeOutputAsync_OverwritesExistingOutput()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var firstOutput = new NodeOutput { Status = "running" };
        var secondOutput = new NodeOutput { Status = "completed" };

        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1", firstOutput);

        // Act
        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1", secondOutput);

        // Assert
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.NodeOutputs["node-1"].Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task MergeNodeOutputAsync_UpdatesTimestamp()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        var context = await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);
        var originalUpdatedAt = context.UpdatedAt;

        await Task.Delay(10);

        // Act
        var output = new NodeOutput { Status = "completed" };
        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1", output);

        // Assert
        var updated = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(updated!.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    [Test]
    public async Task MergeNodeOutputAsync_MultipleNodes_StoresAllOutputs()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act
        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1",
            new NodeOutput { Status = "completed", Data = new Dictionary<string, object> { ["x"] = 1 } });
        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-2",
            new NodeOutput { Status = "completed", Data = new Dictionary<string, object> { ["y"] = 2 } });
        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-3",
            new NodeOutput { Status = "failed", Error = "test error" });

        // Assert
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.Multiple(() =>
        {
            Assert.That(context!.NodeOutputs, Has.Count.EqualTo(3));
            Assert.That(context.NodeOutputs["node-1"].Status, Is.EqualTo("completed"));
            Assert.That(context.NodeOutputs["node-2"].Status, Is.EqualTo("completed"));
            Assert.That(context.NodeOutputs["node-3"].Status, Is.EqualTo("failed"));
            Assert.That(context.NodeOutputs["node-3"].Error, Is.EqualTo("test error"));
        });
    }

    #endregion

    #region AddArtifactAsync Tests

    [Test]
    public async Task AddArtifactAsync_ValidArtifact_AddsAndReturnsTrue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var artifact = new WorkflowArtifact
        {
            Name = "output.json",
            Path = "/path/to/output.json",
            Type = "file",
            Size = 1024,
            ContentType = "application/json"
        };

        // Act
        var result = await _store.AddArtifactAsync(_testProjectPath, "execution-1", artifact);

        // Assert
        Assert.That(result, Is.True);

        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.Multiple(() =>
        {
            Assert.That(context!.Artifacts, Has.Count.EqualTo(1));
            Assert.That(context.Artifacts[0].Name, Is.EqualTo("output.json"));
            Assert.That(context.Artifacts[0].Size, Is.EqualTo(1024));
        });
    }

    [Test]
    public async Task AddArtifactAsync_NonExistentContext_ReturnsFalse()
    {
        // Arrange
        var artifact = new WorkflowArtifact
        {
            Name = "test",
            Path = "/path",
            Type = "file"
        };

        // Act
        var result = await _store.AddArtifactAsync(_testProjectPath, "non-existent", artifact);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AddArtifactAsync_MultipleArtifacts_AddsAll()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act
        await _store.AddArtifactAsync(_testProjectPath, "execution-1", new WorkflowArtifact
        {
            Name = "artifact1.txt",
            Path = "/path/artifact1.txt",
            Type = "file"
        });
        await _store.AddArtifactAsync(_testProjectPath, "execution-1", new WorkflowArtifact
        {
            Name = "artifact2.log",
            Path = "/path/artifact2.log",
            Type = "log"
        });

        // Assert
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AddArtifactAsync_UpdatesTimestamp()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        var context = await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);
        var originalUpdatedAt = context.UpdatedAt;

        await Task.Delay(10);

        // Act
        var artifact = new WorkflowArtifact { Name = "test", Path = "/path", Type = "file" };
        await _store.AddArtifactAsync(_testProjectPath, "execution-1", artifact);

        // Assert
        var updated = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(updated!.UpdatedAt, Is.GreaterThan(originalUpdatedAt));
    }

    #endregion

    #region DeleteContextAsync Tests

    [Test]
    public async Task DeleteContextAsync_ExistingContext_DeletesAndReturnsTrue()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act
        var deleted = await _store.DeleteContextAsync(_testProjectPath, "execution-1");

        // Assert
        Assert.That(deleted, Is.True);

        var retrieved = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(retrieved, Is.Null);
    }

    [Test]
    public async Task DeleteContextAsync_NonExistentContext_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteContextAsync(_testProjectPath, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteContextAsync_RemovesFileFromDisk()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var contextFile = Path.Combine(_testProjectPath, ".fleece", "workflows", "context_execution-1.json");
        Assert.That(File.Exists(contextFile), Is.True);

        // Act
        await _store.DeleteContextAsync(_testProjectPath, "execution-1");

        // Assert
        Assert.That(File.Exists(contextFile), Is.False);
    }

    [Test]
    public async Task DeleteContextAsync_RemovesFromCache()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Ensure it's in cache
        await _store.GetContextAsync(_testProjectPath, "execution-1");

        // Act
        await _store.DeleteContextAsync(_testProjectPath, "execution-1");

        // Assert - Should not find in cache
        var retrieved = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(retrieved, Is.Null);
    }

    #endregion

    #region Persistence Tests

    [Test]
    public async Task Context_PersistsAcrossRestarts()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { issueId = "123" });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        var variable = JsonSerializer.SerializeToElement("persisted");
        await _store.SetValueAsync(_testProjectPath, "execution-1", "myVar", variable);

        await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", "node-1",
            new NodeOutput { Status = "completed", Data = new Dictionary<string, object> { ["x"] = 42 } });

        await _store.AddArtifactAsync(_testProjectPath, "execution-1", new WorkflowArtifact
        {
            Name = "result.json",
            Path = "/path/result.json",
            Type = "file"
        });

        // Dispose and create new store (simulate restart)
        _store.Dispose();
        _store = new WorkflowContextStore(_mockLogger.Object);

        // Act
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ExecutionId, Is.EqualTo("execution-1"));
            Assert.That(context.Variables, Contains.Key("myVar"));
            Assert.That(context.NodeOutputs, Contains.Key("node-1"));
            Assert.That(context.NodeOutputs["node-1"].Status, Is.EqualTo("completed"));
            Assert.That(context.Artifacts, Has.Count.EqualTo(1));
            Assert.That(context.Artifacts[0].Name, Is.EqualTo("result.json"));
        });
    }

    [Test]
    public async Task Context_HandlesCorruptedFile()
    {
        // Arrange - Create a corrupted context file
        var workflowsDir = Path.Combine(_testProjectPath, ".fleece", "workflows");
        Directory.CreateDirectory(workflowsDir);
        var contextFile = Path.Combine(workflowsDir, "context_corrupted-1.json");
        await File.WriteAllTextAsync(contextFile, "{ invalid json");

        // Act
        var context = await _store.GetContextAsync(_testProjectPath, "corrupted-1");

        // Assert - Should return null and not throw
        Assert.That(context, Is.Null);
    }

    #endregion

    #region Concurrent Access Tests

    [Test]
    public async Task ConcurrentSetValues_AllValuesStored()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act - Set multiple values concurrently
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var value = JsonSerializer.SerializeToElement(i);
            await _store.SetValueAsync(_testProjectPath, "execution-1", $"var{i}", value);
        });

        await Task.WhenAll(tasks);

        // Assert - All values should be stored
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.Variables, Has.Count.EqualTo(10));
    }

    [Test]
    public async Task ConcurrentMergeNodeOutputs_AllOutputsStored()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act - Merge multiple outputs concurrently
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var output = new NodeOutput
            {
                Status = "completed",
                Data = new Dictionary<string, object> { ["value"] = i }
            };
            await _store.MergeNodeOutputAsync(_testProjectPath, "execution-1", $"node-{i}", output);
        });

        await Task.WhenAll(tasks);

        // Assert - All outputs should be stored
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.NodeOutputs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task ConcurrentAddArtifacts_AllArtifactsStored()
    {
        // Arrange
        var triggerData = JsonSerializer.SerializeToElement(new { });
        await _store.InitializeContextAsync(
            _testProjectPath,
            "execution-1",
            "workflow-1",
            "/working/dir",
            triggerData);

        // Act - Add multiple artifacts concurrently
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var artifact = new WorkflowArtifact
            {
                Name = $"artifact-{i}.txt",
                Path = $"/path/artifact-{i}.txt",
                Type = "file"
            };
            await _store.AddArtifactAsync(_testProjectPath, "execution-1", artifact);
        });

        await Task.WhenAll(tasks);

        // Assert - All artifacts should be stored
        var context = await _store.GetContextAsync(_testProjectPath, "execution-1");
        Assert.That(context!.Artifacts, Has.Count.EqualTo(5));
    }

    #endregion
}
