using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Notifications;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Roadmap;
using Homespun.Features.Roadmap.Sync;
using Homespun.Tests.Helpers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Project = Homespun.Features.PullRequests.Data.Entities.Project;

namespace Homespun.Tests.Features.Roadmap.Sync;

[TestFixture]
public class RoadmapSyncServiceTests
{
    private TestDataStore _dataStore = null!;
    private Mock<ICommandRunner> _mockRunner = null!;
    private Mock<IGitWorktreeService> _mockWorktreeService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<INotificationService> _mockNotificationService = null!;
    private Mock<IHubContext<NotificationHub>> _mockHubContext = null!;
    private RoadmapSyncService _service = null!;
    private string _tempDir = null!;
    private string _projectDir = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new TestDataStore();
        _mockRunner = new Mock<ICommandRunner>();
        _mockWorktreeService = new Mock<IGitWorktreeService>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockHubContext = new Mock<IHubContext<NotificationHub>>();

        // Create temp directory structure
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _projectDir = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(_projectDir);

        // Setup mock hub context
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _service = new RoadmapSyncService(
            _dataStore,
            _mockRunner.Object,
            _mockWorktreeService.Object,
            _mockGitHubService.Object,
            _mockNotificationService.Object,
            _mockHubContext.Object,
            NullLogger<RoadmapSyncService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "Test Project",
            LocalPath = _projectDir,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    private string GetLocalRoadmapPath()
    {
        return Path.Combine(_tempDir, "ROADMAP.local.json");
    }

    private void CreateLocalRoadmap(string content)
    {
        File.WriteAllText(GetLocalRoadmapPath(), content);
    }

    #region InitializeLocalRoadmapAsync Tests

    [Test]
    public async Task InitializeLocalRoadmap_CreatesFile_WhenNotExists()
    {
        // Arrange
        var project = await CreateTestProject();
        
        var mainRoadmap = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/test",
                    "shortTitle": "test",
                    "group": "core",
                    "type": "feature",
                    "title": "Test Feature",
                    "parents": []
                }
            ]
        }
        """;

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = mainRoadmap });

        // Act
        var result = await _service.InitializeLocalRoadmapAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(File.Exists(GetLocalRoadmapPath()), Is.True);

        var content = await File.ReadAllTextAsync(GetLocalRoadmapPath());
        Assert.That(content, Does.Contain("core/feature/test"));
    }

    [Test]
    public async Task InitializeLocalRoadmap_PreservesFile_WhenExists()
    {
        // Arrange
        var project = await CreateTestProject();
        
        var existingContent = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "existing/feature/test",
                    "shortTitle": "test",
                    "group": "existing",
                    "type": "feature",
                    "title": "Existing Feature",
                    "parents": []
                }
            ]
        }
        """;
        CreateLocalRoadmap(existingContent);

        // Act
        var result = await _service.InitializeLocalRoadmapAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
        
        var content = await File.ReadAllTextAsync(GetLocalRoadmapPath());
        Assert.That(content, Does.Contain("existing/feature/test"));
        
        // git show should not have been called since file exists
        _mockRunner.Verify(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show")), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task InitializeLocalRoadmap_CreatesEmptyRoadmap_WhenMainHasNone()
    {
        // Arrange
        var project = await CreateTestProject();

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: Path 'ROADMAP.json' does not exist" });

        // Act
        var result = await _service.InitializeLocalRoadmapAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(File.Exists(GetLocalRoadmapPath()), Is.True);

        var roadmap = await RoadmapParser.LoadAsync(GetLocalRoadmapPath());
        Assert.That(roadmap.Version, Is.EqualTo("1.1"));
        Assert.That(roadmap.Changes, Is.Empty);
    }

    #endregion

    #region CompareWithMainAsync Tests

    [Test]
    public async Task CompareWithMain_DetectsAddedChanges()
    {
        // Arrange
        var project = await CreateTestProject();
        
        // Local has an extra change
        CreateLocalRoadmap("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/existing",
                    "shortTitle": "existing",
                    "group": "core",
                    "type": "feature",
                    "title": "Existing",
                    "parents": []
                },
                {
                    "id": "core/feature/new",
                    "shortTitle": "new",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature",
                    "parents": []
                }
            ]
        }
        """);

        // Main only has one change
        var mainRoadmap = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/existing",
                    "shortTitle": "existing",
                    "group": "core",
                    "type": "feature",
                    "title": "Existing",
                    "parents": []
                }
            ]
        }
        """;

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = mainRoadmap });

        // Act
        var result = await _service.CompareWithMainAsync(project.Id);

        // Assert
        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.AddedChanges, Has.Count.EqualTo(1));
        Assert.That(result.AddedChanges[0], Is.EqualTo("core/feature/new"));
    }

    [Test]
    public async Task CompareWithMain_DetectsRemovedChanges()
    {
        // Arrange
        var project = await CreateTestProject();
        
        // Local has one change
        CreateLocalRoadmap("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/existing",
                    "shortTitle": "existing",
                    "group": "core",
                    "type": "feature",
                    "title": "Existing",
                    "parents": []
                }
            ]
        }
        """);

        // Main has two changes
        var mainRoadmap = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/existing",
                    "shortTitle": "existing",
                    "group": "core",
                    "type": "feature",
                    "title": "Existing",
                    "parents": []
                },
                {
                    "id": "core/feature/removed",
                    "shortTitle": "removed",
                    "group": "core",
                    "type": "feature",
                    "title": "Removed Feature",
                    "parents": []
                }
            ]
        }
        """;

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = mainRoadmap });

        // Act
        var result = await _service.CompareWithMainAsync(project.Id);

        // Assert
        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedChanges, Has.Count.EqualTo(1));
        Assert.That(result.RemovedChanges[0], Is.EqualTo("core/feature/removed"));
    }

    [Test]
    public async Task CompareWithMain_ReturnsNoChanges_WhenIdentical()
    {
        // Arrange
        var project = await CreateTestProject();
        
        var roadmapContent = """
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/test",
                    "shortTitle": "test",
                    "group": "core",
                    "type": "feature",
                    "title": "Test",
                    "parents": []
                }
            ]
        }
        """;

        CreateLocalRoadmap(roadmapContent);

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = roadmapContent });

        // Act
        var result = await _service.CompareWithMainAsync(project.Id);

        // Assert
        Assert.That(result.HasChanges, Is.False);
        Assert.That(result.AddedChanges, Is.Empty);
        Assert.That(result.RemovedChanges, Is.Empty);
        Assert.That(result.ModifiedChanges, Is.Empty);
    }

    #endregion

    #region GetLocalRoadmapPath Tests

    [Test]
    public async Task GetLocalRoadmapPath_ReturnsCorrectPath()
    {
        // Arrange
        var project = await CreateTestProject();

        // Act
        var path = _service.GetLocalRoadmapPath(project);

        // Assert
        Assert.That(path, Is.EqualTo(Path.Combine(_tempDir, "ROADMAP.local.json")));
    }

    [Test]
    public void GetLocalRoadmapPath_ByProjectId_ReturnsNull_WhenProjectNotFound()
    {
        // Act
        var path = _service.GetLocalRoadmapPath("non-existent");

        // Assert
        Assert.That(path, Is.Null);
    }

    #endregion

    #region HasPendingChangesAsync Tests

    [Test]
    public async Task HasPendingChanges_ReturnsTrue_WhenChangesExist()
    {
        // Arrange
        var project = await CreateTestProject();
        
        CreateLocalRoadmap("""
        {
            "version": "1.1",
            "changes": [
                {
                    "id": "core/feature/new",
                    "shortTitle": "new",
                    "group": "core",
                    "type": "feature",
                    "title": "New Feature",
                    "parents": []
                }
            ]
        }
        """);

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = """{ "version": "1.1", "changes": [] }""" });

        // Act
        var result = await _service.HasPendingChangesAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasPendingChanges_ReturnsFalse_WhenNoChanges()
    {
        // Arrange
        var project = await CreateTestProject();
        
        var roadmapContent = """{ "version": "1.1", "changes": [] }""";
        CreateLocalRoadmap(roadmapContent);

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("show main:ROADMAP.json")), _projectDir))
            .ReturnsAsync(new CommandResult { Success = true, Output = roadmapContent });

        // Act
        var result = await _service.HasPendingChangesAsync(project.Id);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion
}
