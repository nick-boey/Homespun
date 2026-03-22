using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class FleeceChangeDetectionServiceTests
{
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IGitCloneService> _cloneServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IFleeceService> _fleeceServiceMock = null!;
    private Mock<ILogger<FleeceChangeDetectionService>> _loggerMock = null!;
    private FleeceChangeDetectionService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectServiceMock = new Mock<IProjectService>();
        _cloneServiceMock = new Mock<IGitCloneService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _fleeceServiceMock = new Mock<IFleeceService>();
        _loggerMock = new Mock<ILogger<FleeceChangeDetectionService>>();

        _service = new FleeceChangeDetectionService(
            _projectServiceMock.Object,
            _cloneServiceMock.Object,
            _sessionServiceMock.Object,
            _fleeceServiceMock.Object,
            _loggerMock.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-detection-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void SetupProjectAndSession(string projectId, string sessionId, string mainPath, string clonePath)
    {
        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(new Project { Id = projectId, Name = "Test Project", LocalPath = mainPath, DefaultBranch = "main" });

        _sessionServiceMock.Setup(x => x.GetSession(sessionId))
            .Returns(new ClaudeSession
            {
                Id = sessionId,
                ProjectId = projectId,
                WorkingDirectory = clonePath,
                EntityId = "test-entity",
                Model = "sonnet",
                Mode = SessionMode.Build
            });
    }

    private async Task CreateFleeceIssueOnDiskAsync(string basePath, Issue issue)
    {
        await SaveIssuesAsync(basePath, [issue]);
    }

    private async Task SaveIssuesAsync(string basePath, List<Issue> issues)
    {
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storage = new JsonlStorageService(basePath, serializer, schemaValidator);

        await storage.EnsureDirectoryExistsAsync(CancellationToken.None);

        // Load existing issues and append the new ones
        var existingIssues = await storage.LoadIssuesAsync(CancellationToken.None);
        var allIssues = existingIssues.ToList();
        allIssues.AddRange(issues);
        await storage.SaveIssuesAsync(allIssues, CancellationToken.None);
    }

    [Test]
    public async Task DetectChangesAsync_WhenCloneHasNewIssue_ReturnsCreatedChange()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        // Create empty .fleece directory in main
        Directory.CreateDirectory(Path.Combine(mainPath, ".fleece"));

        // Create a new issue in clone
        var newIssue = new Issue
        {
            Id = "newissue",
            Title = "New Feature",
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            Description = "A new feature",
            LastUpdate = DateTimeOffset.UtcNow
        };
        await CreateFleeceIssueOnDiskAsync(clonePath, newIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has no issues - empty .fleece directory created above

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ChangeType, Is.EqualTo(ChangeType.Created));
        Assert.That(result[0].IssueId, Is.EqualTo("newissue"));
        Assert.That(result[0].Title, Is.EqualTo("New Feature"));
    }

    [Test]
    public async Task DetectChangesAsync_WhenCloneModifiesIssue_ReturnsUpdatedChange()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var agentTime = DateTimeOffset.UtcNow;

        var originalIssue = new Issue
        {
            Id = "existing",
            Title = "Original Title",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = baseTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        var modifiedIssue = new Issue
        {
            Id = "existing",
            Title = "Modified Title",
            TitleLastUpdate = agentTime,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Progress,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = agentTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create the original issue in main
        await CreateFleeceIssueOnDiskAsync(mainPath, originalIssue);

        // Create the modified issue in clone
        await CreateFleeceIssueOnDiskAsync(clonePath, modifiedIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(result[0].IssueId, Is.EqualTo("existing"));
        Assert.That(result[0].FieldChanges, Has.Count.EqualTo(2)); // Title and Status
        Assert.That(result[0].FieldChanges.Any(f => f.FieldName == "Title"), Is.True);
        Assert.That(result[0].FieldChanges.Any(f => f.FieldName == "Status"), Is.True);
    }

    [Test]
    public async Task DetectChangesAsync_WhenFleeceDirectoryMissing_ReturnsEmptyChanges()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);
        // Deliberately not creating .fleece directory

        var mainIssue = new Issue
        {
            Id = "main-issue",
            Title = "Main Issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Write issues to main (this creates the .fleece directory)
        await CreateFleeceIssueOnDiskAsync(mainPath, mainIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        // No issues in clone means no changes detected (main issues are not deleted)
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task DetectChangesAsync_WhenIssueMarkedAsDeleted_ReturnsDeletedChange()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var agentTime = DateTimeOffset.UtcNow;

        var originalIssue = new Issue
        {
            Id = "to-delete",
            Title = "Issue to Delete",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = baseTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        var deletedIssue = new Issue
        {
            Id = "to-delete",
            Title = "Issue to Delete",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Status = IssueStatus.Deleted,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = agentTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create the original issue in main
        await CreateFleeceIssueOnDiskAsync(mainPath, originalIssue);

        // Create the deleted issue in clone
        await CreateFleeceIssueOnDiskAsync(clonePath, deletedIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        // The service detects both an Update (status changed) and a Deleted change
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Updated && c.IssueId == "to-delete"), Is.True);
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Deleted && c.IssueId == "to-delete"), Is.True);
    }

    [Test]
    public async Task DetectChangesAsync_WhenNoChanges_ReturnsEmptyList()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var issue = new Issue
        {
            Id = "unchanged",
            Title = "Unchanged Issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Create the same issue in main and clone
        await CreateFleeceIssueOnDiskAsync(mainPath, issue);
        await CreateFleeceIssueOnDiskAsync(clonePath, issue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DetectChangesAsync_WhenProjectNotFound_ThrowsArgumentException()
    {
        // Arrange
        var projectId = "nonexistent";
        var sessionId = "session-1";

        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync((Project?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(
            async () => await _service.DetectChangesAsync(projectId, sessionId));

        Assert.That(ex!.Message, Does.Contain("Project"));
    }

    [Test]
    public void DetectChangesAsync_WhenSessionNotFound_ThrowsArgumentException()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "nonexistent";

        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(new Project { Id = projectId, Name = "Test Project", LocalPath = "/test/path", DefaultBranch = "main" });

        _sessionServiceMock.Setup(x => x.GetSession(sessionId))
            .Returns((ClaudeSession?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(
            async () => await _service.DetectChangesAsync(projectId, sessionId));

        Assert.That(ex!.Message, Does.Contain("Session"));
    }

    [Test]
    [TestCase(IssueStatus.Progress, Description = "Status serialized as 'progress' should be parsed")]
    [TestCase(IssueStatus.Review, Description = "Status serialized as 'review' should be parsed")]
    [TestCase(IssueStatus.Complete, Description = "Status serialized as 'complete' should be parsed")]
    public async Task DetectChangesAsync_WhenIssueHasStringEnumStatus_ParsesCorrectly(IssueStatus status)
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        // Create an issue with the specified status using JsonlSerializer (writes string enums)
        var issue = new Issue
        {
            Id = "status-test",
            Title = "Status Test Issue",
            Status = status,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };
        await CreateFleeceIssueOnDiskAsync(clonePath, issue);

        // Create empty .fleece directory in main (no issues)
        Directory.CreateDirectory(Path.Combine(mainPath, ".fleece"));

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act - This should not throw a JsonException when parsing string enum values
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ChangeType, Is.EqualTo(ChangeType.Created));
        Assert.That(result[0].IssueId, Is.EqualTo("status-test"));
        Assert.That(result[0].ModifiedIssue!.Status, Is.EqualTo(status));
    }

    [Test]
    public async Task DetectChangesAsync_WithMultipleChanges_ReturnsAllChanges()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var agentTime = DateTimeOffset.UtcNow;

        var existingIssue = new Issue
        {
            Id = "existing",
            Title = "Existing Issue",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = baseTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        var modifiedIssue = new Issue
        {
            Id = "existing",
            Title = "Modified Issue",
            TitleLastUpdate = agentTime,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Progress,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = agentTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        var newIssue = new Issue
        {
            Id = "new-one",
            Title = "New Issue",
            TitleLastUpdate = agentTime,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Open,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Feature,
            TypeLastUpdate = agentTime,
            TypeModifiedBy = "agent",
            LastUpdate = agentTime,
            CreatedAt = agentTime,
            CreatedBy = "agent"
        };

        // Create existing issue in main
        await CreateFleeceIssueOnDiskAsync(mainPath, existingIssue);

        // Create issues in clone
        await CreateFleeceIssueOnDiskAsync(clonePath, modifiedIssue);
        await CreateFleeceIssueOnDiskAsync(clonePath, newIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Created && c.IssueId == "new-one"), Is.True);
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Updated && c.IssueId == "existing"), Is.True);
    }

    [Test]
    public async Task DetectChangesAsync_WhenBothSidesModifySameIssue_UsesMergedResult()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Main has newer title
        var mainIssue = new Issue
        {
            Id = "merge-test",
            Title = "Title from Main",
            TitleLastUpdate = DateTimeOffset.UtcNow.AddMinutes(-2),
            TitleModifiedBy = "main-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = DateTimeOffset.UtcNow.AddMinutes(-2),
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Agent has newer status
        var agentIssue = new Issue
        {
            Id = "merge-test",
            Title = "Original Title",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Status = IssueStatus.Progress,
            StatusLastUpdate = DateTimeOffset.UtcNow.AddMinutes(-1),
            StatusModifiedBy = "agent-user",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create issues on disk
        await CreateFleeceIssueOnDiskAsync(mainPath, mainIssue);
        await CreateFleeceIssueOnDiskAsync(clonePath, agentIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var change = result[0];
        Assert.That(change.ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(change.IssueId, Is.EqualTo("merge-test"));

        // The merged result should have main's title (newer) and agent's status (newer)
        Assert.That(change.ModifiedIssue!.Title, Is.EqualTo("Title from Main"));
        Assert.That(change.ModifiedIssue!.Status, Is.EqualTo(IssueStatus.Progress));

        // Original issue should be main state
        Assert.That(change.OriginalIssue!.Title, Is.EqualTo("Title from Main"));
        Assert.That(change.OriginalIssue!.Status, Is.EqualTo(IssueStatus.Open));

        // Field changes should reflect main to merged diff
        Assert.That(change.FieldChanges.Count, Is.EqualTo(1));
        Assert.That(change.FieldChanges[0].FieldName, Is.EqualTo("Status"));
        Assert.That(change.FieldChanges[0].OldValue, Is.EqualTo("Open"));
        Assert.That(change.FieldChanges[0].NewValue, Is.EqualTo("Progress"));
    }

    [Test]
    public async Task DetectChangesAsync_WhenMainHasNewerTimestamp_PreservesMainValue()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Main has newer title (should win)
        var mainIssue = new Issue
        {
            Id = "main-wins",
            Title = "Newer Title from Main",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "main-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Agent has older title (should lose)
        var agentIssue = new Issue
        {
            Id = "main-wins",
            Title = "Older Title from Agent",
            TitleLastUpdate = baseTime.AddMinutes(1),
            TitleModifiedBy = "agent-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = baseTime.AddMinutes(1),
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create issues on disk
        await CreateFleeceIssueOnDiskAsync(mainPath, mainIssue);
        await CreateFleeceIssueOnDiskAsync(clonePath, agentIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert - no changes detected because merged equals main
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task DetectChangesAsync_WhenAgentHasNewerTimestamp_PreservesAgentValue()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Main has older title
        var mainIssue = new Issue
        {
            Id = "agent-wins",
            Title = "Older Title from Main",
            TitleLastUpdate = baseTime.AddMinutes(1),
            TitleModifiedBy = "main-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = baseTime.AddMinutes(1),
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Agent has newer title (should win)
        var agentIssue = new Issue
        {
            Id = "agent-wins",
            Title = "Newer Title from Agent",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "agent-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create issues on disk
        await CreateFleeceIssueOnDiskAsync(mainPath, mainIssue);
        await CreateFleeceIssueOnDiskAsync(clonePath, agentIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert - change detected because agent's title is newer
        Assert.That(result, Has.Count.EqualTo(1));
        var change = result[0];
        Assert.That(change.ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(change.ModifiedIssue!.Title, Is.EqualTo("Newer Title from Agent"));
        Assert.That(change.FieldChanges.Any(f => f.FieldName == "Title"), Is.True);
    }

    [Test]
    public async Task DetectChangesAsync_WhenDifferentFieldsModified_MergesBothChanges()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var mainChangeTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        var agentChangeTime = DateTimeOffset.UtcNow.AddMinutes(-2);

        // Main modified description
        var mainIssue = new Issue
        {
            Id = "field-merge",
            Title = "Original Title",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Description = "Description from Main",
            DescriptionLastUpdate = mainChangeTime,
            DescriptionModifiedBy = "main-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = mainChangeTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Agent modified type
        var agentIssue = new Issue
        {
            Id = "field-merge",
            Title = "Original Title",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Description = "Original Description",
            DescriptionLastUpdate = baseTime,
            DescriptionModifiedBy = "original",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Bug,
            TypeLastUpdate = agentChangeTime,
            TypeModifiedBy = "agent-user",
            LastUpdate = agentChangeTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Create issues on disk
        await CreateFleeceIssueOnDiskAsync(mainPath, mainIssue);
        await CreateFleeceIssueOnDiskAsync(clonePath, agentIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert - merged result should have main's description and agent's type
        Assert.That(result, Has.Count.EqualTo(1));
        var change = result[0];
        Assert.That(change.ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(change.ModifiedIssue!.Description, Is.EqualTo("Description from Main"));
        Assert.That(change.ModifiedIssue!.Type, Is.EqualTo(IssueType.Bug));

        // Only Type should appear as a change (main to merged diff)
        Assert.That(change.FieldChanges.Any(f => f.FieldName == "Type"), Is.True);
        Assert.That(change.FieldChanges.All(f => f.FieldName != "Description"), Is.True);
    }
}
