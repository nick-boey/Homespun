using TreeAgent.Web.Services;
using TreeAgent.Web.Tests.Integration.Fixtures;

namespace TreeAgent.Web.Tests.Integration;

/// <summary>
/// Integration tests for GitWorktreeService that test against real git repositories.
/// These tests require git to be installed and available on the PATH.
/// </summary>
[Trait("Category", "Integration")]
public class GitWorktreeServiceIntegrationTests : IDisposable
{
    private readonly TempGitRepositoryFixture _fixture;
    private readonly GitWorktreeService _service;

    public GitWorktreeServiceIntegrationTests()
    {
        _fixture = new TempGitRepositoryFixture();
        _service = new GitWorktreeService(); // Uses real CommandRunner
    }

    /// <summary>
    /// Normalizes a path for comparison (git returns forward slashes on Windows).
    /// </summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    [Fact]
    public async Task CreateWorktree_WithExistingBranch_CreatesWorktreeSuccessfully()
    {
        // Arrange
        var branchName = "feature/test-worktree";
        _fixture.CreateBranch(branchName);

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.NotNull(worktreePath);
        Assert.True(Directory.Exists(worktreePath));
        Assert.True(File.Exists(Path.Combine(worktreePath, "README.md")));
    }

    [Fact]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchAndWorktree()
    {
        // Arrange
        var branchName = "feature/new-branch";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert
        Assert.NotNull(worktreePath);
        Assert.True(Directory.Exists(worktreePath));

        // Verify the branch was created
        var branches = _fixture.RunGit("branch --list");
        Assert.Contains(branchName, branches);
    }

    [Fact]
    public async Task CreateWorktree_WithBaseBranch_CreatesBranchFromSpecifiedBase()
    {
        // Arrange
        var baseBranch = "develop";
        var featureBranch = "feature/from-develop";

        // Create develop branch with additional content
        _fixture.CreateBranch(baseBranch, checkout: true);
        _fixture.CreateFileAndCommit("develop.txt", "Develop content", "Add develop file");
        _fixture.RunGit("checkout -"); // Go back to main/master

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            featureBranch,
            createBranch: true,
            baseBranch: baseBranch);

        // Assert
        Assert.NotNull(worktreePath);
        Assert.True(Directory.Exists(worktreePath));
        // The worktree should have the file from develop branch
        Assert.True(File.Exists(Path.Combine(worktreePath, "develop.txt")));
    }

    [Fact]
    public async Task CreateWorktree_NonExistentBranch_ReturnsNull()
    {
        // Arrange
        var branchName = "non-existent-branch";

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: false);

        // Assert
        Assert.Null(worktreePath);
    }

    [Fact]
    public async Task ListWorktrees_ReturnsMainWorktree()
    {
        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.NotNull(worktrees);
        Assert.Single(worktrees);
        Assert.Equal(NormalizePath(_fixture.RepositoryPath), NormalizePath(worktrees[0].Path));
    }

    [Fact]
    public async Task ListWorktrees_AfterCreatingWorktree_ReturnsMultipleWorktrees()
    {
        // Arrange
        var branchName = "feature/list-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.NotNull(worktrees);
        Assert.Equal(2, worktrees.Count);
        Assert.Contains(worktrees, w => NormalizePath(w.Path) == NormalizePath(_fixture.RepositoryPath));
        Assert.Contains(worktrees, w => w.Branch?.EndsWith(branchName) == true);
    }

    [Fact]
    public async Task ListWorktrees_ReturnsCorrectBranchInfo()
    {
        // Arrange
        var branchName = "feature/branch-info-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.NotNull(worktreePath);

        // Act
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);

        // Assert
        var worktree = worktrees.FirstOrDefault(w => NormalizePath(w.Path) == NormalizePath(worktreePath));
        Assert.NotNull(worktree);
        Assert.EndsWith(branchName, worktree.Branch!);
        Assert.NotNull(worktree.HeadCommit);
        Assert.False(worktree.IsDetached);
    }

    [Fact]
    public async Task RemoveWorktree_ExistingWorktree_RemovesSuccessfully()
    {
        // Arrange
        var branchName = "feature/remove-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.NotNull(worktreePath);
        Assert.True(Directory.Exists(worktreePath));

        // Act
        var result = await _service.RemoveWorktreeAsync(_fixture.RepositoryPath, worktreePath);

        // Assert
        Assert.True(result);
        Assert.False(Directory.Exists(worktreePath));
    }

    [Fact]
    public async Task RemoveWorktree_NonExistentWorktree_ReturnsFalse()
    {
        // Arrange
        var fakeWorktreePath = Path.Combine(_fixture.RepositoryPath, ".worktrees", "non-existent");

        // Act
        var result = await _service.RemoveWorktreeAsync(_fixture.RepositoryPath, fakeWorktreePath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task WorktreeExists_ExistingWorktree_ReturnsTrue()
    {
        // Arrange
        var branchName = "feature/exists-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Act
        var exists = await _service.WorktreeExistsAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task WorktreeExists_NonExistentWorktree_ReturnsFalse()
    {
        // Act
        var exists = await _service.WorktreeExistsAsync(_fixture.RepositoryPath, "non-existent-branch");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task PruneWorktrees_RemovesStaleWorktreeReferences()
    {
        // Arrange
        var branchName = "feature/prune-test";
        _fixture.CreateBranch(branchName);
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);
        Assert.NotNull(worktreePath);

        // Manually delete the worktree directory (simulating stale worktree)
        if (Directory.Exists(worktreePath))
        {
            Directory.Delete(worktreePath, recursive: true);
        }

        // Act - should not throw
        await _service.PruneWorktreesAsync(_fixture.RepositoryPath);

        // Assert - worktree should be pruned from the list
        var worktrees = await _service.ListWorktreesAsync(_fixture.RepositoryPath);
        Assert.DoesNotContain(worktrees, w => NormalizePath(w.Path) == NormalizePath(worktreePath));
    }

    [Fact]
    public async Task CreateWorktree_WithModifiedFiles_WorktreeHasCleanState()
    {
        // Arrange
        var branchName = "feature/clean-state-test";
        _fixture.CreateBranch(branchName);

        // Modify a file in the main worktree (uncommitted change)
        var readmePath = Path.Combine(_fixture.RepositoryPath, "README.md");
        File.AppendAllText(readmePath, "\n\nModified content.");

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.NotNull(worktreePath);

        // The new worktree should have the clean version from the branch
        var worktreeReadme = File.ReadAllText(Path.Combine(worktreePath, "README.md"));
        Assert.DoesNotContain("Modified content.", worktreeReadme);
    }

    [Fact]
    public async Task CreateWorktree_BranchNameWithSpecialCharacters_SanitizesPath()
    {
        // Arrange
        var branchName = "feature/special@chars#test";
        _fixture.RunGit($"branch \"{branchName}\"");

        // Act
        var worktreePath = await _service.CreateWorktreeAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.NotNull(worktreePath);
        Assert.DoesNotContain("@", worktreePath);
        Assert.DoesNotContain("#", worktreePath);
        Assert.True(Directory.Exists(worktreePath));
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
