using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class IssueWorkspaceServiceTests
{
    private string _testBaseDir = null!;
    private Mock<ICommandRunner> _commandRunnerMock = null!;
    private Mock<ILogger<IssueWorkspaceService>> _loggerMock = null!;
    private IssueWorkspaceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), $"issue-workspace-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBaseDir);
        _commandRunnerMock = new Mock<ICommandRunner>();
        _loggerMock = new Mock<ILogger<IssueWorkspaceService>>();

        // Default: commands succeed
        _commandRunnerMock
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "", ExitCode = 0 });

        _service = new IssueWorkspaceService(_testBaseDir, _commandRunnerMock.Object, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testBaseDir))
        {
            Directory.Delete(_testBaseDir, recursive: true);
        }
    }

    #region Path Resolution Tests

    [Test]
    public void GetIssueWorkspace_NonExistentIssue_ReturnsNull()
    {
        var result = _service.GetIssueWorkspace("my-project", "abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetIssueWorkspace_AfterEnsure_ReturnsCorrectPaths()
    {
        // Arrange
        await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        // Act
        var result = _service.GetIssueWorkspace("my-project", "abc123");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.IssueId, Is.EqualTo("abc123"));
            Assert.That(result.BranchName, Is.EqualTo("feature/my-branch"));
            Assert.That(result.ClaudePath, Is.EqualTo(Path.Combine(_testBaseDir, "my-project", "issues", "abc123", ".claude")));
            Assert.That(result.SessionsPath, Is.EqualTo(Path.Combine(_testBaseDir, "my-project", "issues", "abc123", ".sessions")));
            Assert.That(result.SourcePath, Is.EqualTo(Path.Combine(_testBaseDir, "my-project", "issues", "abc123", "src")));
        });
    }

    #endregion

    #region EnsureProjectSetupAsync Tests

    [Test]
    public async Task EnsureProjectSetupAsync_CreatesMainDirectory()
    {
        await _service.EnsureProjectSetupAsync("proj-1", "my-project", "https://github.com/user/repo.git", "main");

        var mainDir = Path.Combine(_testBaseDir, "my-project", "main");
        Assert.That(Directory.Exists(mainDir), Is.True);
    }

    [Test]
    public async Task EnsureProjectSetupAsync_ClonesRepo_WhenNotExists()
    {
        await _service.EnsureProjectSetupAsync("proj-1", "my-project", "https://github.com/user/repo.git", "main");

        var mainDir = Path.Combine(_testBaseDir, "my-project", "main");
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("clone") && args.Contains("https://github.com/user/repo.git") && args.Contains(mainDir)),
            It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public async Task EnsureProjectSetupAsync_PullsExistingRepo_WhenDotGitExists()
    {
        // Arrange: Create .git directory to simulate existing clone
        var mainDir = Path.Combine(_testBaseDir, "my-project", "main");
        Directory.CreateDirectory(Path.Combine(mainDir, ".git"));

        // Act
        await _service.EnsureProjectSetupAsync("proj-1", "my-project", "https://github.com/user/repo.git", "main");

        // Assert: pull was called, not clone
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("pull")),
            mainDir),
            Times.Once);
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("clone")),
            It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task EnsureProjectSetupAsync_IsIdempotent()
    {
        await _service.EnsureProjectSetupAsync("proj-1", "my-project", "https://github.com/user/repo.git", "main");

        // Simulate .git directory was created by clone
        var mainDir = Path.Combine(_testBaseDir, "my-project", "main");
        Directory.CreateDirectory(Path.Combine(mainDir, ".git"));

        await _service.EnsureProjectSetupAsync("proj-1", "my-project", "https://github.com/user/repo.git", "main");

        // Second call should pull, not clone
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("clone")),
            It.IsAny<string>()),
            Times.Once);
    }

    #endregion

    #region EnsureIssueWorkspaceAsync Tests

    [Test]
    public async Task EnsureIssueWorkspaceAsync_CreatesDirectoryStructure()
    {
        var workspace = await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(workspace.ClaudePath), Is.True);
            Assert.That(Directory.Exists(workspace.SessionsPath), Is.True);
            // src directory is created by git clone, but parent should exist
            Assert.That(Directory.Exists(Path.GetDirectoryName(workspace.SourcePath)), Is.True);
        });
    }

    [Test]
    public async Task EnsureIssueWorkspaceAsync_ClonesRepo_WhenNotExists()
    {
        var workspace = await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("clone") && args.Contains("https://github.com/user/repo.git") && args.Contains(workspace.SourcePath)),
            It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public async Task EnsureIssueWorkspaceAsync_ChecksOutBranch()
    {
        var workspace = await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("checkout") && args.Contains("feature/my-branch")),
            workspace.SourcePath),
            Times.Once);
    }

    [Test]
    public async Task EnsureIssueWorkspaceAsync_CreatesNewBranch_WhenCheckoutFails()
    {
        // Arrange: reset default mock and set up specific behavior
        _commandRunnerMock.Reset();

        // Default: all commands succeed
        _commandRunnerMock
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "", ExitCode = 0 });

        // Override: plain checkout (without -b) fails
        _commandRunnerMock
            .Setup(r => r.RunAsync(
                "git",
                It.Is<string>(args => args.StartsWith("checkout ") && !args.StartsWith("checkout -b")),
                It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = false, ExitCode = 1, Error = "error: pathspec did not match" });

        // Act
        var workspace = await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        // Assert
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("checkout -b") && args.Contains("feature/my-branch")),
            workspace.SourcePath),
            Times.Once);
    }

    [Test]
    public async Task EnsureIssueWorkspaceAsync_ReturnsCorrectWorkspace()
    {
        var workspace = await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        Assert.Multiple(() =>
        {
            Assert.That(workspace.IssueId, Is.EqualTo("abc123"));
            Assert.That(workspace.BranchName, Is.EqualTo("feature/my-branch"));
            Assert.That(workspace.ClaudePath, Does.EndWith(Path.Combine("abc123", ".claude")));
            Assert.That(workspace.SessionsPath, Does.EndWith(Path.Combine("abc123", ".sessions")));
            Assert.That(workspace.SourcePath, Does.EndWith(Path.Combine("abc123", "src")));
        });
    }

    [Test]
    public async Task EnsureIssueWorkspaceAsync_IsIdempotent_SkipsClone_WhenSrcExists()
    {
        // First call
        await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        // Simulate .git directory was created by clone
        var srcDir = Path.Combine(_testBaseDir, "my-project", "issues", "abc123", "src");
        Directory.CreateDirectory(Path.Combine(srcDir, ".git"));

        // Second call
        await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");

        // Clone should only be called once
        _commandRunnerMock.Verify(r => r.RunAsync(
            "git",
            It.Is<string>(args => args.Contains("clone")),
            It.IsAny<string>()),
            Times.Once);
    }

    #endregion

    #region CleanupIssueWorkspaceAsync Tests

    [Test]
    public async Task CleanupIssueWorkspaceAsync_RemovesIssueDirectory()
    {
        // Arrange: Create workspace
        await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");
        var issueDir = Path.Combine(_testBaseDir, "my-project", "issues", "abc123");
        Assert.That(Directory.Exists(issueDir), Is.True);

        // Act
        await _service.CleanupIssueWorkspaceAsync("my-project", "abc123");

        // Assert
        Assert.That(Directory.Exists(issueDir), Is.False);
    }

    [Test]
    public async Task CleanupIssueWorkspaceAsync_NonExistentIssue_DoesNotThrow()
    {
        // Should not throw for non-existent workspace
        Assert.DoesNotThrowAsync(async () =>
            await _service.CleanupIssueWorkspaceAsync("my-project", "nonexistent"));
    }

    [Test]
    public async Task CleanupIssueWorkspaceAsync_GetWorkspace_ReturnsNull_AfterCleanup()
    {
        // Arrange
        await _service.EnsureIssueWorkspaceAsync("my-project", "abc123", "https://github.com/user/repo.git", "feature/my-branch");
        Assert.That(_service.GetIssueWorkspace("my-project", "abc123"), Is.Not.Null);

        // Act
        await _service.CleanupIssueWorkspaceAsync("my-project", "abc123");

        // Assert
        Assert.That(_service.GetIssueWorkspace("my-project", "abc123"), Is.Null);
    }

    #endregion

    #region Multiple Issues Isolation Tests

    [Test]
    public async Task MultipleIssues_HaveIsolatedWorkspaces()
    {
        var ws1 = await _service.EnsureIssueWorkspaceAsync("my-project", "issue-1", "https://github.com/user/repo.git", "branch-1");
        var ws2 = await _service.EnsureIssueWorkspaceAsync("my-project", "issue-2", "https://github.com/user/repo.git", "branch-2");

        Assert.Multiple(() =>
        {
            Assert.That(ws1.SourcePath, Is.Not.EqualTo(ws2.SourcePath));
            Assert.That(ws1.ClaudePath, Is.Not.EqualTo(ws2.ClaudePath));
            Assert.That(ws1.SessionsPath, Is.Not.EqualTo(ws2.SessionsPath));
        });
    }

    [Test]
    public async Task CleanupOneIssue_DoesNotAffectOther()
    {
        await _service.EnsureIssueWorkspaceAsync("my-project", "issue-1", "https://github.com/user/repo.git", "branch-1");
        await _service.EnsureIssueWorkspaceAsync("my-project", "issue-2", "https://github.com/user/repo.git", "branch-2");

        await _service.CleanupIssueWorkspaceAsync("my-project", "issue-1");

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetIssueWorkspace("my-project", "issue-1"), Is.Null);
            Assert.That(_service.GetIssueWorkspace("my-project", "issue-2"), Is.Not.Null);
        });
    }

    #endregion
}
