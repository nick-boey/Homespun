using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Projects;

[TestFixture]
public class ProjectServiceTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<ICommandRunner> _mockCommandRunner = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<ILogger<ProjectService>> _mockLogger = null!;
    private ProjectService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _dataStore = new MockDataStore();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockCommandRunner = new Mock<ICommandRunner>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ProjectService>>();

        // Default configuration - no overrides, use default paths
        _mockConfiguration.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);

        _service = new ProjectService(
            _dataStore,
            _mockGitHubService.Object,
            _mockCommandRunner.Object,
            _mockConfiguration.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    #region CreateAsync Tests

    [Test]
    public async Task CreateAsync_ValidOwnerRepo_ReturnsSuccessWithProject()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("microsoft", "vscode"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("microsoft/vscode");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Project, Is.Not.Null);
        Assert.That(result.Project!.Name, Is.EqualTo("vscode"));
        Assert.That(result.Project.GitHubOwner, Is.EqualTo("microsoft"));
        Assert.That(result.Project.GitHubRepo, Is.EqualTo("vscode"));
        Assert.That(result.Project.DefaultBranch, Is.EqualTo("main"));
    }

    [Test]
    public async Task CreateAsync_ValidOwnerRepo_SetsCorrectLocalPath()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        var expectedPathEnd = Path.Combine(".homespun", "projects", "repo", "main");
        Assert.That(result.Project!.LocalPath, Does.EndWith(expectedPathEnd.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Test]
    public async Task CreateAsync_InvalidFormat_ReturnsError()
    {
        // Arrange - no mocking needed, should fail validation

        // Act
        var result = await _service.CreateAsync("invalid-format");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_EmptyOwner_ReturnsError()
    {
        // Act
        var result = await _service.CreateAsync("/repo");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_EmptyRepo_ReturnsError()
    {
        // Act
        var result = await _service.CreateAsync("owner/");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid format"));
    }

    [Test]
    public async Task CreateAsync_GitHubReturnsNull_ReturnsError()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "nonexistent"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.CreateAsync("owner/nonexistent");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Could not fetch repository"));
    }

    [Test]
    public async Task CreateAsync_CloneFails_ReturnsError()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = false, Error = "Clone failed" });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Failed to clone"));
    }

    [Test]
    public async Task CreateAsync_Success_AddsProjectToDataStore()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(_dataStore.Projects, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_WithNonMainDefaultBranch_UsesCorrectBranch()
    {
        // Arrange
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("owner", "repo"))
            .ReturnsAsync("develop");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateAsync("owner/repo");

        // Assert
        Assert.That(result.Project!.DefaultBranch, Is.EqualTo("develop"));
        Assert.That(result.Project.LocalPath, Does.Contain("develop"));
    }

    [Test]
    public async Task CreateAsync_RepoNameWithDot_CreatesValidProject()
    {
        // T035: Repo names containing `.` (e.g. `foo.js`) must round-trip without
        // being stripped or sanitised — they are valid GitHub repository names.
        _mockGitHubService.Setup(s => s.GetDefaultBranchAsync("vercel", "next.js"))
            .ReturnsAsync("main");
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        var result = await _service.CreateAsync("vercel/next.js");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Project, Is.Not.Null);
        Assert.That(result.Project!.Name, Is.EqualTo("next.js"));
        Assert.That(result.Project.GitHubRepo, Is.EqualTo("next.js"));
        Assert.That(result.Project.LocalPath, Does.Contain("next.js"));
    }

    #endregion

    #region CreateLocalAsync Edge Tests (T045)

    [Test]
    public async Task CreateLocalAsync_EmptyDefaultBranch_FallsBackToMain()
    {
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true });

        var name = "local-empty-branch-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var result = await _service.CreateLocalAsync(name, defaultBranch: "   ");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Project!.DefaultBranch, Is.EqualTo("main"));
            Assert.That(result.Project.LocalPath, Does.EndWith(Path.Combine(name, "main")));
            _mockCommandRunner.Verify(
                r => r.RunAsync("git", It.Is<string>(cmd => cmd.Contains("branch -M main")), It.IsAny<string>()),
                Times.Once);
        }
        finally
        {
            CleanupLocalRepo(name);
        }
    }

    [Test]
    public async Task CreateLocalAsync_DirectoryAlreadyExists_ReturnsError()
    {
        var name = "local-exists-" + Guid.NewGuid().ToString("N")[..8];
        var homespunBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homespun", "projects");
        var existingPath = Path.Combine(homespunBasePath, name, "main");
        Directory.CreateDirectory(existingPath);

        try
        {
            var result = await _service.CreateLocalAsync(name);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("already exists"));
            _mockCommandRunner.Verify(
                r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }
        finally
        {
            CleanupLocalRepo(name);
        }
    }

    [Test]
    public async Task CreateLocalAsync_GitInitFails_CleansUpDirectory()
    {
        _mockCommandRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s == "init"), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = false, Error = "init exploded" });

        var name = "local-init-fail-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var result = await _service.CreateLocalAsync(name);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Failed to initialize git"));

            var homespunBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".homespun", "projects");
            var expectedPath = Path.Combine(homespunBasePath, name, "main");
            Assert.That(Directory.Exists(expectedPath), Is.False,
                "localPath should be cleaned up after git init failure");
        }
        finally
        {
            CleanupLocalRepo(name);
        }
    }

    private static void CleanupLocalRepo(string name)
    {
        var homespunBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".homespun", "projects");
        var repoPath = Path.Combine(homespunBasePath, name);
        if (Directory.Exists(repoPath))
        {
            try { Directory.Delete(repoPath, true); } catch { /* best-effort */ }
        }
    }

    #endregion

    #region UpdateAsync Tests

    [Test]
    public async Task UpdateAsync_ValidProject_UpdatesDefaultModel()
    {
        // Arrange
        var project = new Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);

        // Act
        var result = await _service.UpdateAsync(project.Id, "sonnet");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DefaultModel, Is.EqualTo("sonnet"));
    }

    [Test]
    public async Task UpdateAsync_NonExistentProject_ReturnsNull()
    {
        // Act
        var result = await _service.UpdateAsync("nonexistent", "model");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_BumpsUpdatedAt()
    {
        // T063: UpdatedAt should be stamped on every successful update.
        var project = new Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main",
            UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await _dataStore.AddProjectAsync(project);
        var before = project.UpdatedAt;

        var result = await _service.UpdateAsync(project.Id, "sonnet");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UpdatedAt, Is.GreaterThan(before));
    }

    [Test]
    public async Task UpdateAsync_NullDefaultModel_ClearsPreviousValue()
    {
        // T063: Passing null for DefaultModel must clear the previously-set value.
        var project = new Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main",
            DefaultModel = "sonnet"
        };
        await _dataStore.AddProjectAsync(project);

        var result = await _service.UpdateAsync(project.Id, defaultModel: null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DefaultModel, Is.Null);
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_ExistingProject_ReturnsTrue()
    {
        // Arrange
        var project = new Project
        {
            Name = "repo",
            LocalPath = "/path",
            GitHubOwner = "owner",
            GitHubRepo = "repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);

        // Act
        var result = await _service.DeleteAsync(project.Id);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_dataStore.Projects, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task DeleteAsync_NonExistentProject_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteAsync("nonexistent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DoesNotTouchLocalPath()
    {
        // T054 / spec §A-2 / FR-009: DeleteAsync removes the data-store record
        // only. The clone on disk at LocalPath is intentionally preserved —
        // it may hold uncommitted work or be referenced by Git-slice worktrees.
        var tempRoot = Path.Combine(Path.GetTempPath(), "homespun-delete-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);
        var sentinelFile = Path.Combine(tempRoot, "uncommitted.txt");
        await File.WriteAllTextAsync(sentinelFile, "work in progress");

        var project = new Project
        {
            Name = "delete-preserves-fs",
            LocalPath = tempRoot,
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project);

        try
        {
            var deleted = await _service.DeleteAsync(project.Id);

            Assert.That(deleted, Is.True);
            Assert.That(_dataStore.GetProject(project.Id), Is.Null);
            Assert.That(Directory.Exists(tempRoot), Is.True,
                "LocalPath must survive DeleteAsync (spec §A-2, FR-009)");
            Assert.That(File.Exists(sentinelFile), Is.True,
                "Files inside LocalPath must survive DeleteAsync");
            _mockCommandRunner.Verify(
                r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never,
                "DeleteAsync must not shell out — no rm/git clean calls");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { /* best-effort */ }
            }
        }
    }

    #endregion

    #region GetAllAsync Tests

    [Test]
    public async Task GetAllAsync_ReturnsAllProjects()
    {
        // Arrange
        var project1 = new Project
        {
            Name = "repo1",
            LocalPath = "/path1",
            GitHubOwner = "owner",
            GitHubRepo = "repo1",
            DefaultBranch = "main"
        };
        var project2 = new Project
        {
            Name = "repo2",
            LocalPath = "/path2",
            GitHubOwner = "owner",
            GitHubRepo = "repo2",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(project1);
        await _dataStore.AddProjectAsync(project2);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_OrdersByUpdatedAtDescending()
    {
        // T016: Two projects with distinct UpdatedAt must be returned newest-first.
        var older = new Project
        {
            Name = "older",
            LocalPath = "/old",
            DefaultBranch = "main",
            UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var newer = new Project
        {
            Name = "newer",
            LocalPath = "/new",
            DefaultBranch = "main",
            UpdatedAt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        await _dataStore.AddProjectAsync(older);
        await _dataStore.AddProjectAsync(newer);

        var result = await _service.GetAllAsync();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo(newer.Id), "Newer project should come first");
        Assert.That(result[1].Id, Is.EqualTo(older.Id));
    }

    #endregion
}
