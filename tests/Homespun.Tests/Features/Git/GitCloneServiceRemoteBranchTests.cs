using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class GitCloneServiceRemoteBranchTests
{
    private Mock<ICommandRunner> _mockRunner = null!;
    private GitCloneService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new GitCloneService(_mockRunner.Object, Mock.Of<ILogger<GitCloneService>>());
        _tempDir = Path.Combine(Path.GetTempPath(), "homespun-test-" + Guid.NewGuid().ToString("N")[..8]);
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

    [Test]
    public async Task EnsureBranchAvailableAsync_LocalBranchExists_DoesNotFetch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/existing";

        // Mock ListLocalBranchesAsync to return the branch as existing
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"'{branchName}|abc123|||2024-01-01|Initial commit'"
            });

        // Act
        await _service.EnsureBranchAvailableAsync(repoPath, branchName);

        // Assert - fetch should NOT have been called
        _mockRunner.Verify(
            r => r.RunAsync("git", It.Is<string>(s => s.Contains("fetch")), repoPath),
            Times.Never);
    }

    [Test]
    public async Task EnsureBranchAvailableAsync_RemoteOnlyBranch_FetchesBranch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/remote-only";

        // Mock ListLocalBranchesAsync to return empty (branch doesn't exist locally)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" }); // No local branches match

        // Mock fetch to succeed
        _mockRunner.Setup(r => r.RunAsync("git",
                $"fetch origin {branchName}:{branchName}", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        await _service.EnsureBranchAvailableAsync(repoPath, branchName);

        // Assert - fetch SHOULD have been called
        _mockRunner.Verify(
            r => r.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath),
            Times.Once);
    }

    [Test]
    public async Task CreateCloneAsync_WithNonDefaultBaseBranch_EnsuresBranchAvailable()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/new-branch";
        var baseBranch = "feature/parent-branch";

        // Mock for EnsureBranchAvailableAsync - base branch doesn't exist locally
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock fetch for base branch
        _mockRunner.Setup(r => r.RunAsync("git",
                $"fetch origin {baseBranch}:{baseBranch}", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock branch creation
        _mockRunner.Setup(r => r.RunAsync("git",
                $"branch \"{branchName}\" \"{baseBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock clone --local
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url in workdir
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.StartsWith("remote set-url")),
                It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock checkout in workdir
        _mockRunner.Setup(r => r.RunAsync("git",
                $"checkout \"{branchName}\"",
                It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName, createBranch: true, baseBranch: baseBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Verify that fetch was called to ensure base branch is available
        _mockRunner.Verify(
            r => r.RunAsync("git", $"fetch origin {baseBranch}:{baseBranch}", repoPath),
            Times.Once);
    }

    [Test]
    public async Task CreateCloneAsync_WithBaseBranchAlreadyLocal_DoesNotFetch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/new-branch";
        var baseBranch = "feature/local-parent";

        // Mock for EnsureBranchAvailableAsync - base branch exists locally
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"'{baseBranch}|def456|||2024-01-01|Some commit'"
            });

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock branch creation
        _mockRunner.Setup(r => r.RunAsync("git",
                $"branch \"{branchName}\" \"{baseBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock clone --local
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url in workdir
        _mockRunner.Setup(r => r.RunAsync("git",
                It.Is<string>(s => s.StartsWith("remote set-url")),
                It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock checkout in workdir
        _mockRunner.Setup(r => r.RunAsync("git",
                $"checkout \"{branchName}\"",
                It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName, createBranch: true, baseBranch: baseBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Verify that fetch was NOT called since base branch exists locally
        _mockRunner.Verify(
            r => r.RunAsync("git", $"fetch origin {baseBranch}:{baseBranch}", repoPath),
            Times.Never);
    }
}
