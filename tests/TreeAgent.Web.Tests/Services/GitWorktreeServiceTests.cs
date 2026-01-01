using Moq;
using TreeAgent.Web.Services;

namespace TreeAgent.Web.Tests.Services;

public class GitWorktreeServiceTests
{
    private readonly Mock<ICommandRunner> _mockRunner;
    private readonly GitWorktreeService _service;

    public GitWorktreeServiceTests()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new GitWorktreeService(_mockRunner.Object);
    }

    [Fact]
    public async Task CreateWorktree_Success_ReturnsPath()
    {
        // Arrange
        var repoPath = "/repo";
        var branchName = "feature/test";
        var expectedWorktreePath = "/repo/.worktrees/feature-test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("feature-test", result);
    }

    [Fact]
    public async Task CreateWorktree_GitError_ReturnsNull()
    {
        // Arrange
        var repoPath = "/repo";
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: 'feature/test' is already checked out" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveWorktree_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = "/repo";
        var worktreePath = "/repo/.worktrees/feature-test";

        _mockRunner.Setup(r => r.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveWorktree_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = "/repo";
        var worktreePath = "/repo/.worktrees/feature-test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ListWorktrees_Success_ReturnsWorktrees()
    {
        // Arrange
        var repoPath = "/repo";
        var gitOutput = "/repo\n/repo/.worktrees/feature-1\n/repo/.worktrees/feature-2\n";

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "worktree /repo\nworktree /repo/.worktrees/feature-1\nworktree /repo/.worktrees/feature-2" });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListWorktrees_GitError_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = "/repo";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PruneWorktrees_CallsGitPrune()
    {
        // Arrange
        var repoPath = "/repo";

        _mockRunner.Setup(r => r.RunAsync("git", "worktree prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.PruneWorktreesAsync(repoPath);

        // Assert
        _mockRunner.Verify(r => r.RunAsync("git", "worktree prune", repoPath), Times.Once);
    }

    [Fact]
    public void SanitizeBranchName_RemovesSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/new-thing");

        // Assert
        Assert.Equal("feature-new-thing", result);
    }

    [Fact]
    public void SanitizeBranchName_RemovesSpecialCharacters()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/test@branch#1");

        // Assert
        Assert.Equal("feature-test-branch-1", result);
    }

    [Fact]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchFirst()
    {
        // Arrange
        var repoPath = "/repo";
        var branchName = "feature/new-branch";

        _mockRunner.SetupSequence(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true }) // branch check
            .ReturnsAsync(new CommandResult { Success = true }) // create branch
            .ReturnsAsync(new CommandResult { Success = true }); // create worktree

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName, createBranch: true);

        // Assert
        Assert.NotNull(result);
    }
}
