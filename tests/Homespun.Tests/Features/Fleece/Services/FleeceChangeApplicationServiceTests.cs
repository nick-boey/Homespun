using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class FleeceChangeApplicationServiceTests
{
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IProjectFleeceService> _fleeceServiceMock = null!;
    private Mock<IFleeceChangeDetectionService> _changeDetectionServiceMock = null!;
    private Mock<IFleeceConflictDetectionService> _conflictDetectionServiceMock = null!;
    private Mock<ILogger<FleeceChangeApplicationService>> _loggerMock = null!;
    private FleeceChangeApplicationService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _projectServiceMock = new Mock<IProjectService>();
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _fleeceServiceMock = new Mock<IProjectFleeceService>();
        _changeDetectionServiceMock = new Mock<IFleeceChangeDetectionService>();
        _conflictDetectionServiceMock = new Mock<IFleeceConflictDetectionService>();
        _loggerMock = new Mock<ILogger<FleeceChangeApplicationService>>();

        _service = new FleeceChangeApplicationService(
            _projectServiceMock.Object,
            _sessionServiceMock.Object,
            _fleeceServiceMock.Object,
            _changeDetectionServiceMock.Object,
            _conflictDetectionServiceMock.Object,
            _loggerMock.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-application-test-{Guid.NewGuid():N}");
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
                Mode = SessionMode.Build,
                Status = ClaudeSessionStatus.Stopped
            });
    }

    private static async Task SaveIssuesAsync(string basePath, List<Issue> issues)
    {
        await FleeceFileHelper.SaveIssuesAsync(basePath, issues);
    }

    private static async Task<List<Issue>> LoadIssuesAsync(string basePath)
    {
        return (await FleeceFileHelper.LoadIssuesAsync(basePath)).ToList();
    }

    [Test]
    public async Task ApplyChangesAsync_WithAgentWinsStrategy_UsesFileMerge()
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

        var mainIssue = new Issue
        {
            Id = "issue-1",
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

        var agentIssue = new Issue
        {
            Id = "issue-1",
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

        await SaveIssuesAsync(mainPath, [mainIssue]);
        await SaveIssuesAsync(clonePath, [agentIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        // Mock change detection to return a change
        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "issue-1",
                ChangeType = ChangeType.Updated,
                Title = "Modified Title"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Changes, Has.Count.EqualTo(1));
        Assert.That(result.Changes[0].ChangeType, Is.EqualTo(ChangeType.Updated));
        Assert.That(result.Changes[0].IssueId, Is.EqualTo("issue-1"));

        // Verify the issue was actually merged to disk
        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(1));
        Assert.That(mergedIssues[0].Title, Is.EqualTo("Modified Title"));
        Assert.That(mergedIssues[0].Status, Is.EqualTo(IssueStatus.Progress));

        // Verify ReloadFromDiskAsync was called
        _fleeceServiceMock.Verify(x => x.ReloadFromDiskAsync(mainPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ApplyChangesAsync_WithNewIssueFromAgent_CreatesIssue()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        // Create empty .fleece in main
        Directory.CreateDirectory(Path.Combine(mainPath, ".fleece"));

        var agentTime = DateTimeOffset.UtcNow;

        var newIssue = new Issue
        {
            Id = "new-issue",
            Title = "New Feature",
            TitleLastUpdate = agentTime,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Open,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Feature,
            TypeLastUpdate = agentTime,
            TypeModifiedBy = "agent",
            Description = "A new feature created by agent",
            DescriptionLastUpdate = agentTime,
            DescriptionModifiedBy = "agent",
            LastUpdate = agentTime,
            CreatedAt = agentTime,
            CreatedBy = "agent"
        };

        await SaveIssuesAsync(clonePath, [newIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "new-issue",
                ChangeType = ChangeType.Created,
                Title = "New Feature"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Changes, Has.Count.EqualTo(1));
        Assert.That(result.Changes[0].ChangeType, Is.EqualTo(ChangeType.Created));
        Assert.That(result.Changes[0].IssueId, Is.EqualTo("new-issue"));

        // Verify the issue was created on disk
        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(1));
        Assert.That(mergedIssues[0].Id, Is.EqualTo("new-issue"));
        Assert.That(mergedIssues[0].Title, Is.EqualTo("New Feature"));
    }

    [Test]
    public async Task ApplyChangesAsync_WithBothSidesModifyingDifferentFields_MergesBoth()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var mainChangeTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var agentChangeTime = DateTimeOffset.UtcNow.AddMinutes(-3);

        // Main modified description
        var mainIssue = new Issue
        {
            Id = "merge-test",
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

        // Agent modified status
        var agentIssue = new Issue
        {
            Id = "merge-test",
            Title = "Original Title",
            TitleLastUpdate = baseTime,
            TitleModifiedBy = "original",
            Description = "Original Description",
            DescriptionLastUpdate = baseTime,
            DescriptionModifiedBy = "original",
            Status = IssueStatus.Progress,
            StatusLastUpdate = agentChangeTime,
            StatusModifiedBy = "agent-user",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = agentChangeTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        await SaveIssuesAsync(mainPath, [mainIssue]);
        await SaveIssuesAsync(clonePath, [agentIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "merge-test",
                ChangeType = ChangeType.Updated,
                Title = "Original Title"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify merged result has main's description (newer) and agent's status (newer)
        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(1));
        Assert.That(mergedIssues[0].Description, Is.EqualTo("Description from Main"));
        Assert.That(mergedIssues[0].Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task ApplyChangesAsync_WithTimestampBasedConflict_NewerTimestampWins()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var olderTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newerTime = DateTimeOffset.UtcNow.AddMinutes(-2);

        // Main has newer title
        var mainIssue = new Issue
        {
            Id = "conflict-test",
            Title = "Newer Title from Main",
            TitleLastUpdate = newerTime,
            TitleModifiedBy = "main-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = newerTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        // Agent has older title
        var agentIssue = new Issue
        {
            Id = "conflict-test",
            Title = "Older Title from Agent",
            TitleLastUpdate = olderTime,
            TitleModifiedBy = "agent-user",
            Status = IssueStatus.Open,
            StatusLastUpdate = baseTime,
            StatusModifiedBy = "original",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = olderTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        await SaveIssuesAsync(mainPath, [mainIssue]);
        await SaveIssuesAsync(clonePath, [agentIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "conflict-test",
                ChangeType = ChangeType.Updated,
                Title = "Older Title from Agent"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify main's title (newer) was preserved
        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(1));
        Assert.That(mergedIssues[0].Title, Is.EqualTo("Newer Title from Main"));
    }

    [Test]
    public async Task ApplyChangesAsync_WithDryRun_DoesNotModifyFiles()
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

        var mainIssue = new Issue
        {
            Id = "issue-1",
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

        var agentIssue = new Issue
        {
            Id = "issue-1",
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

        await SaveIssuesAsync(mainPath, [mainIssue]);
        await SaveIssuesAsync(clonePath, [agentIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "issue-1",
                ChangeType = ChangeType.Updated,
                Title = "Modified Title"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins, dryRun: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.WouldApply, Is.True);
        Assert.That(result.Changes, Has.Count.EqualTo(1));

        // Verify file was NOT modified
        var mainIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mainIssues, Has.Count.EqualTo(1));
        Assert.That(mainIssues[0].Title, Is.EqualTo("Original Title"));

        // Verify ReloadFromDiskAsync was NOT called
        _fleeceServiceMock.Verify(x => x.ReloadFromDiskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ApplyChangesAsync_WhenNoChangesDetected_ReturnsSuccessWithEmptyChanges()
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

        await SaveIssuesAsync(mainPath, [issue]);
        await SaveIssuesAsync(clonePath, [issue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("No changes detected"));
        Assert.That(result.Changes, Is.Empty);
        Assert.That(result.WouldApply, Is.False);
    }

    [Test]
    public async Task ApplyChangesAsync_WhenProjectNotFound_ReturnsFailure()
    {
        // Arrange
        var projectId = "nonexistent";
        var sessionId = "session-1";

        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync((Project?)null);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task ApplyChangesAsync_WhenSessionNotFound_ReturnsFailure()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "nonexistent";

        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(new Project { Id = projectId, Name = "Test", LocalPath = "/test", DefaultBranch = "main" });

        _sessionServiceMock.Setup(x => x.GetSession(sessionId))
            .Returns((ClaudeSession?)null);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task ApplyChangesAsync_WhenSessionIsRunning_ReturnsFailure()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        _projectServiceMock.Setup(x => x.GetByIdAsync(projectId))
            .ReturnsAsync(new Project { Id = projectId, Name = "Test", LocalPath = mainPath, DefaultBranch = "main" });

        _sessionServiceMock.Setup(x => x.GetSession(sessionId))
            .Returns(new ClaudeSession
            {
                Id = sessionId,
                ProjectId = projectId,
                WorkingDirectory = clonePath,
                EntityId = "test-entity",
                Model = "sonnet",
                Mode = SessionMode.Build,
                Status = ClaudeSessionStatus.Running // Session is still running
            });

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Cannot apply changes while session is active"));
    }

    [Test]
    public async Task ApplyChangesAsync_PreservesIssuesNotTouchedByAgent()
    {
        // Arrange
        var projectId = "proj-1";
        var sessionId = "session-1";
        var mainPath = Path.Combine(_tempDir, "main");
        var clonePath = Path.Combine(_tempDir, "clone");

        Directory.CreateDirectory(mainPath);
        Directory.CreateDirectory(clonePath);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        var mainOnlyIssue = new Issue
        {
            Id = "main-only",
            Title = "Issue Only in Main",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            LastUpdate = baseTime,
            CreatedAt = baseTime,
            CreatedBy = "main"
        };

        var sharedIssue = new Issue
        {
            Id = "shared",
            Title = "Shared Issue",
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

        var agentModifiedIssue = new Issue
        {
            Id = "shared",
            Title = "Modified Shared Issue",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Progress,
            StatusLastUpdate = DateTimeOffset.UtcNow,
            StatusModifiedBy = "agent",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        await SaveIssuesAsync(mainPath, [mainOnlyIssue, sharedIssue]);
        await SaveIssuesAsync(clonePath, [agentModifiedIssue]); // Agent only has the shared issue

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IssueChangeDto
            {
                IssueId = "shared",
                ChangeType = ChangeType.Updated,
                Title = "Modified Shared Issue"
            }]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);

        // Verify both issues are in the result
        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(2));

        var mainOnlyResult = mergedIssues.FirstOrDefault(i => i.Id == "main-only");
        var sharedResult = mergedIssues.FirstOrDefault(i => i.Id == "shared");

        Assert.That(mainOnlyResult, Is.Not.Null);
        Assert.That(mainOnlyResult!.Title, Is.EqualTo("Issue Only in Main"));

        Assert.That(sharedResult, Is.Not.Null);
        Assert.That(sharedResult!.Title, Is.EqualTo("Modified Shared Issue"));
        Assert.That(sharedResult.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task ApplyChangesAsync_WithMultipleIssues_AppliesAllChanges()
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

        var mainIssue1 = new Issue
        {
            Id = "issue-1",
            Title = "Issue 1",
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

        var mainIssue2 = new Issue
        {
            Id = "issue-2",
            Title = "Issue 2",
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

        var agentIssue1 = new Issue
        {
            Id = "issue-1",
            Title = "Modified Issue 1",
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

        var agentIssue2 = new Issue
        {
            Id = "issue-2",
            Title = "Modified Issue 2",
            TitleLastUpdate = agentTime,
            TitleModifiedBy = "agent",
            Status = IssueStatus.Complete,
            StatusLastUpdate = agentTime,
            StatusModifiedBy = "agent",
            Type = IssueType.Task,
            TypeLastUpdate = baseTime,
            TypeModifiedBy = "original",
            LastUpdate = agentTime,
            CreatedAt = baseTime,
            CreatedBy = "original"
        };

        var newAgentIssue = new Issue
        {
            Id = "issue-3",
            Title = "New Issue 3",
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

        await SaveIssuesAsync(mainPath, [mainIssue1, mainIssue2]);
        await SaveIssuesAsync(clonePath, [agentIssue1, agentIssue2, newAgentIssue]);

        SetupProjectAndSession(projectId, sessionId, mainPath, clonePath);

        _changeDetectionServiceMock.Setup(x => x.DetectChangesAsync(projectId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new IssueChangeDto { IssueId = "issue-1", ChangeType = ChangeType.Updated, Title = "Modified Issue 1" },
                new IssueChangeDto { IssueId = "issue-2", ChangeType = ChangeType.Updated, Title = "Modified Issue 2" },
                new IssueChangeDto { IssueId = "issue-3", ChangeType = ChangeType.Created, Title = "New Issue 3" }
            ]);

        _conflictDetectionServiceMock.Setup(x => x.DetectConflictsAsync(
                projectId, sessionId, It.IsAny<List<IssueChangeDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.ApplyChangesAsync(
            projectId, sessionId, ConflictResolutionStrategy.AgentWins);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Changes, Has.Count.EqualTo(3));

        var mergedIssues = await LoadIssuesAsync(mainPath);
        Assert.That(mergedIssues, Has.Count.EqualTo(3));

        var issue1 = mergedIssues.First(i => i.Id == "issue-1");
        var issue2 = mergedIssues.First(i => i.Id == "issue-2");
        var issue3 = mergedIssues.First(i => i.Id == "issue-3");

        Assert.That(issue1.Title, Is.EqualTo("Modified Issue 1"));
        Assert.That(issue1.Status, Is.EqualTo(IssueStatus.Progress));

        Assert.That(issue2.Title, Is.EqualTo("Modified Issue 2"));
        Assert.That(issue2.Status, Is.EqualTo(IssueStatus.Complete));

        Assert.That(issue3.Title, Is.EqualTo("New Issue 3"));
        Assert.That(issue3.Type, Is.EqualTo(IssueType.Feature));
    }
}
