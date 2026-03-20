using System.Text.Json;
using Fleece.Core.Models;
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

    private void CreateFleeceIssueOnDisk(string basePath, Issue issue)
    {
        var fleeceDir = Path.Combine(basePath, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        var issueFile = Path.Combine(fleeceDir, "issues_test.jsonl");
        var json = JsonSerializer.Serialize(issue, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.AppendAllText(issueFile, json + Environment.NewLine);
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
        CreateFleeceIssueOnDisk(clonePath, newIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has no issues
        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue>());

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

        var originalIssue = new Issue
        {
            Id = "existing",
            Title = "Original Title",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        var modifiedIssue = new Issue
        {
            Id = "existing",
            Title = "Modified Title",
            Status = IssueStatus.Progress,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Create the modified issue in clone
        CreateFleeceIssueOnDisk(clonePath, modifiedIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has the original issue
        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { originalIssue });

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

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        var mainIssue = new Issue
        {
            Id = "main-issue",
            Title = "Main Issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { mainIssue });

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

        var originalIssue = new Issue
        {
            Id = "to-delete",
            Title = "Issue to Delete",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        var deletedIssue = new Issue
        {
            Id = "to-delete",
            Title = "Issue to Delete",
            Status = IssueStatus.Deleted,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Create the deleted issue in clone
        CreateFleeceIssueOnDisk(clonePath, deletedIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has the original issue
        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { originalIssue });

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

        // Create the same issue in clone
        CreateFleeceIssueOnDisk(clonePath, issue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has the same issue
        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { issue });

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
    public async Task DetectChangesAsync_WithMultipleChanges_ReturnsAllChanges()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var existingIssue = new Issue
        {
            Id = "existing",
            Title = "Existing Issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        var modifiedIssue = new Issue
        {
            Id = "existing",
            Title = "Modified Issue",
            Status = IssueStatus.Progress,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow
        };

        var newIssue = new Issue
        {
            Id = "new-one",
            Title = "New Issue",
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            LastUpdate = DateTimeOffset.UtcNow
        };

        // Create issues in clone
        CreateFleeceIssueOnDisk(clonePath, modifiedIssue);
        CreateFleeceIssueOnDisk(clonePath, newIssue);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Main branch has the original issue
        _fleeceServiceMock.Setup(x => x.ListIssuesAsync(mainPath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Issue> { existingIssue });

        // Act
        var result = await _service.DetectChangesAsync(projectId, sessionId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Created && c.IssueId == "new-one"), Is.True);
        Assert.That(result.Any(c => c.ChangeType == ChangeType.Updated && c.IssueId == "existing"), Is.True);
    }
}
