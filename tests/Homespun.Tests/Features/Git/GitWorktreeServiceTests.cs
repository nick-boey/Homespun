using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class GitWorktreeServiceTests
{
    private Mock<ICommandRunner> _mockRunner = null!;
    private GitWorktreeService _service = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new GitWorktreeService(_mockRunner.Object, Mock.Of<ILogger<GitWorktreeService>>());
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
    public async Task CreateWorktree_Success_ReturnsPath()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Worktree should be in .worktrees directory with flattened name (/ becomes +)
        Assert.That(result, Does.Contain(".worktrees"));
        Assert.That(result, Does.Contain("feature+test"));
    }

    [Test]
    public async Task CreateWorktree_GitError_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: 'feature/test' is already checked out" });

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveWorktree_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktreePath = Path.Combine(repoPath, ".worktrees", "feature-test");

        _mockRunner.Setup(r => r.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RemoveWorktree_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktreePath = Path.Combine(repoPath, ".worktrees", "feature-test");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.RemoveWorktreeAsync(repoPath, worktreePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ListWorktrees_Success_ReturnsWorktrees()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktree1 = Path.Combine(repoPath, ".worktrees", "feature-1");
        var worktree2 = Path.Combine(repoPath, ".worktrees", "feature-2");

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = $"worktree {repoPath}\nworktree {worktree1}\nworktree {worktree2}" });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListWorktrees_GitError_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.ListWorktreesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task PruneWorktrees_CallsGitPrune()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "worktree prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        await _service.PruneWorktreesAsync(repoPath);

        // Assert
        _mockRunner.Verify(r => r.RunAsync("git", "worktree prune", repoPath), Times.Once);
    }

    [Test]
    public void SanitizeBranchName_PreservesSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/new-thing");

        // Assert - slashes are preserved for git branch names
        Assert.That(result, Is.EqualTo("feature/new-thing"));
    }

    [Test]
    public void SanitizeBranchName_RemovesSpecialCharactersButPreservesSlashesAndPlus()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("feature/test@branch#1");

        // Assert - slashes preserved, special chars replaced with dashes
        Assert.That(result, Is.EqualTo("feature/test-branch-1"));
    }

    [Test]
    public void SanitizeBranchName_NormalizesBackslashesToForwardSlashes()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("app\\feature\\test");

        // Assert - backslashes converted to forward slashes
        Assert.That(result, Is.EqualTo("app/feature/test"));
    }

    [Test]
    public void SanitizeBranchName_TrimsSlashesFromEnds()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchName("/feature/test/");

        // Assert - leading/trailing slashes removed
        Assert.That(result, Is.EqualTo("feature/test"));
    }

    [Test]
    public void SanitizeBranchName_WithPlusCharacter_PreservesPlus()
    {
        // Act - Plus character is used to separate branch name from issue ID
        var result = GitWorktreeService.SanitizeBranchName("feature/improve-tool-output+aLP3LH");

        // Assert - Plus should be preserved for branch name matching
        Assert.That(result, Is.EqualTo("feature/improve-tool-output+aLP3LH"));
    }

    #region SanitizeBranchNameForWorktree Tests

    [Test]
    public void SanitizeBranchNameForWorktree_ConvertsSlashesToPlus()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("feature/new-thing");

        // Assert - slashes converted to plus for flat folder structure
        Assert.That(result, Is.EqualTo("feature+new-thing"));
    }

    [Test]
    public void SanitizeBranchNameForWorktree_PreservesPlus()
    {
        // Act - Full branch name with issue ID
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("feature/improve-tool-output+aLP3LH");

        // Assert - Slashes become plus, existing plus is preserved
        Assert.That(result, Is.EqualTo("feature+improve-tool-output+aLP3LH"));
    }

    [Test]
    public void SanitizeBranchNameForWorktree_RemovesSpecialCharacters()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("feature/test@branch#1");

        // Assert - slashes become plus, special chars replaced with dashes
        Assert.That(result, Is.EqualTo("feature+test-branch-1"));
    }

    [Test]
    public void SanitizeBranchNameForWorktree_NormalizesBackslashesToPlus()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("app\\feature\\test");

        // Assert - backslashes converted to plus
        Assert.That(result, Is.EqualTo("app+feature+test"));
    }

    [Test]
    public void SanitizeBranchNameForWorktree_RemovesConsecutivePlus()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("feature//test");

        // Assert - consecutive slashes (now plus) are collapsed
        Assert.That(result, Is.EqualTo("feature+test"));
    }

    [Test]
    public void SanitizeBranchNameForWorktree_TrimsFromEnds()
    {
        // Act
        var result = GitWorktreeService.SanitizeBranchNameForWorktree("/feature/test/");

        // Assert - leading/trailing slashes (now plus) are trimmed
        Assert.That(result, Is.EqualTo("feature+test"));
    }

    #endregion

    [Test]
    public async Task CreateWorktree_WithNewBranch_CreatesBranchFirst()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/new-branch";

        _mockRunner.SetupSequence(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true }) // branch check
            .ReturnsAsync(new CommandResult { Success = true }) // create branch
            .ReturnsAsync(new CommandResult { Success = true }); // create worktree

        // Act
        var result = await _service.CreateWorktreeAsync(repoPath, branchName, createBranch: true);

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task FetchAndUpdateBranchAsync_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var branchName = "main";

        _mockRunner.Setup(r => r.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.FetchAndUpdateBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task FetchAndUpdateBranchAsync_FetchFails_FallsBackToSimpleFetch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var branchName = "main";

        // First fetch command fails (branch might be checked out)
        _mockRunner.Setup(r => r.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "cannot lock ref" });

        // Simple fetch succeeds
        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.FetchAndUpdateBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.True);
        _mockRunner.Verify(r => r.RunAsync("git", "fetch origin", repoPath), Times.Once);
    }

    [Test]
    public async Task FetchAndUpdateBranchAsync_BothFetchesFail_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var branchName = "main";

        _mockRunner.Setup(r => r.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "some error" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "network error" });

        // Act
        var result = await _service.FetchAndUpdateBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.False);
    }

    #region GetWorktreePathForBranchAsync Tests

    [Test]
    [Description("Regression test for bug MlB2lN: Worktree lookup should match branch when git returns refs/heads/ format but we search with short name")]
    public async Task GetWorktreePathForBranchAsync_WithRefsHeadsFormat_MatchesShortBranchName()
    {
        // Arrange - This is the exact scenario from bug MlB2lN
        // Git returns branch as "refs/heads/feature/my-pr" but we search with "feature/my-pr"
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var shortBranchName = "feature/my-pr-branch";
        var expectedPath = Path.Combine(_tempDir, ".worktrees", "feature+my-pr-branch");

        // Mock git worktree list returning the full refs/heads/ format
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {expectedPath}\nbranch refs/heads/{shortBranchName}"
            });

        // Act - Search using short branch name (as PullRequest.BranchName would be)
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, shortBranchName);

        // Assert - Should find the worktree despite format difference
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    [Description("Regression test for bug MlB2lN: WorktreeExists should work with refs/heads/ format")]
    public async Task WorktreeExistsAsync_WithRefsHeadsFormat_ReturnsTrue()
    {
        // Arrange - Verifying WorktreeExistsAsync handles the format correctly (it did before the bug)
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var shortBranchName = "feature/my-pr-branch";
        var worktreePath = Path.Combine(_tempDir, ".worktrees", "feature+my-pr-branch");

        // Mock git worktree list returning the full refs/heads/ format
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {worktreePath}\nbranch refs/heads/{shortBranchName}"
            });

        // Act - Check existence using short branch name
        var result = await _service.WorktreeExistsAsync(repoPath, shortBranchName);

        // Assert - Should find the worktree
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GetWorktreePathForBranchAsync_WithDirectBranchMatch_ReturnsPath()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";
        var expectedPath = Path.Combine(_tempDir, ".worktrees", "feature+test");

        // Mock git worktree list to return a worktree with matching branch
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {expectedPath}\nbranch refs/heads/{branchName}"
            });

        // Act
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    public async Task GetWorktreePathForBranchAsync_WithBranchNameContainingPlus_MatchesByBranch()
    {
        // Arrange - Branch name has + which is preserved in both branch name and worktree path
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Branch name with + character (new flat format)
        var branchName = "feature/improve-tool-output+aLP3LH";

        // The worktree is created with flattened path (slashes become +, existing + is preserved)
        var worktreePath = Path.Combine(_tempDir, ".worktrees", "feature+improve-tool-output+aLP3LH");

        // Mock git worktree list - branch name is preserved with +
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {worktreePath}\nbranch refs/heads/{branchName}"
            });

        // Act
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, branchName);

        // Assert - Should find by direct branch match
        Assert.That(result, Is.EqualTo(worktreePath));
    }

    [Test]
    public async Task GetWorktreePathForBranchAsync_WithFlattenedPath_FallsBackToPathMatch()
    {
        // Arrange - Test the path-based fallback when branch doesn't match directly
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Branch name with + character
        var branchName = "feature/improve-tool-output+aLP3LH";

        // The worktree was created with flattened path in .worktrees directory
        var flattenedPath = Path.GetFullPath(Path.Combine(_tempDir, ".worktrees", "feature+improve-tool-output+aLP3LH"));

        // Mock git worktree list - worktree exists at flattened path but with a different branch name
        // This simulates a case where git reports a slightly different branch name
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {flattenedPath}\nbranch refs/heads/some-other-branch"
            });

        // Act
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, branchName);

        // Assert - Should find by flattened path match
        Assert.That(result, Is.Not.Null);
        Assert.That(Path.GetFullPath(result!), Is.EqualTo(flattenedPath).IgnoreCase);
    }

    [Test]
    public async Task GetWorktreePathForBranchAsync_NoMatchingWorktree_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/nonexistent+test";

        // Mock git worktree list - only main worktree exists
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {repoPath}\nbranch refs/heads/main"
            });

        // Act
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetWorktreePathForBranchAsync_GitCommandFails_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "git error" });

        // Act
        var result = await _service.GetWorktreePathForBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region ListLocalBranchesAsync Tests

    [Test]
    public async Task ListLocalBranchesAsync_Success_ReturnsBranchInfoList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        // Mock worktree list (empty)
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = $"worktree {repoPath}\nHEAD abc123\nbranch refs/heads/main" });

        // Mock for-each-ref for branches
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main|abc1234|origin/main|[ahead 1]|2024-01-15T10:30:00|Initial commit'\n'feature/test|def5678||[behind 2]|2024-01-16T11:00:00|Add feature'"
            });

        // Mock current branch
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });

        // Act
        var result = await _service.ListLocalBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));

        var mainBranch = result.FirstOrDefault(b => b.ShortName == "main");
        Assert.That(mainBranch, Is.Not.Null);
        Assert.That(mainBranch!.IsCurrent, Is.True);
        Assert.That(mainBranch.CommitSha, Is.EqualTo("abc1234"));
        Assert.That(mainBranch.AheadCount, Is.EqualTo(1));

        var featureBranch = result.FirstOrDefault(b => b.ShortName == "feature/test");
        Assert.That(featureBranch, Is.Not.Null);
        Assert.That(featureBranch!.IsCurrent, Is.False);
        Assert.That(featureBranch.BehindCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ListLocalBranchesAsync_GitError_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.ListLocalBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListLocalBranchesAsync_WithWorktree_SetsHasWorktreeFlag()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var worktreePath = Path.Combine(_tempDir, "feature-worktree");

        // Mock worktree list with a worktree for feature/test branch
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {repoPath}\nHEAD abc123\nbranch refs/heads/main\n\nworktree {worktreePath}\nHEAD def456\nbranch refs/heads/feature/test"
            });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main|abc123|||2024-01-15|Commit 1'\n'feature/test|def456|||2024-01-16|Commit 2'"
            });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });

        // Act
        var result = await _service.ListLocalBranchesAsync(repoPath);

        // Assert
        var featureBranch = result.FirstOrDefault(b => b.ShortName == "feature/test");
        Assert.That(featureBranch, Is.Not.Null);
        Assert.That(featureBranch!.HasWorktree, Is.True);
        Assert.That(featureBranch.WorktreePath, Is.EqualTo(worktreePath));
    }

    #endregion

    #region ListRemoteOnlyBranchesAsync Tests

    [Test]
    public async Task ListRemoteOnlyBranchesAsync_ReturnsRemoteOnlyBranches()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        // Mock fetch
        _mockRunner.Setup(r => r.RunAsync("git", "fetch --prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock remote branches
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("refs/remotes/origin")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'origin/main'\n'origin/feature/remote-only'\n'origin/develop'"
            });

        // Mock local branches
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("refs/heads")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main'\n'develop'"
            });

        // Act
        var result = await _service.ListRemoteOnlyBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Does.Contain("feature/remote-only"));
    }

    [Test]
    public async Task ListRemoteOnlyBranchesAsync_FiltersHEAD()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "fetch --prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("refs/remotes/origin")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'origin/HEAD'\n'origin/main'"
            });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("refs/heads")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.ListRemoteOnlyBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Does.Not.Contain("HEAD"));
        Assert.That(result, Does.Contain("main"));
    }

    [Test]
    public async Task ListRemoteOnlyBranchesAsync_GitError_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "fetch --prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("refs/remotes")), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "error" });

        // Act
        var result = await _service.ListRemoteOnlyBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region IsBranchMergedAsync Tests

    [Test]
    public async Task IsBranchMergedAsync_BranchIsMerged_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "merge-base --is-ancestor \"feature/merged\" \"main\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.IsBranchMergedAsync(repoPath, "feature/merged", "main");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsBranchMergedAsync_BranchNotMerged_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "merge-base --is-ancestor \"feature/unmerged\" \"main\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "exit code 1" });

        // Act
        var result = await _service.IsBranchMergedAsync(repoPath, "feature/unmerged", "main");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region DeleteLocalBranchAsync Tests

    [Test]
    public async Task DeleteLocalBranchAsync_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "branch -d \"feature/to-delete\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DeleteLocalBranchAsync(repoPath, "feature/to-delete");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteLocalBranchAsync_ForceDelete_UsesDFlag()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "branch -D \"feature/unmerged\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DeleteLocalBranchAsync(repoPath, "feature/unmerged", force: true);

        // Assert
        Assert.That(result, Is.True);
        _mockRunner.Verify(r => r.RunAsync("git", "branch -D \"feature/unmerged\"", repoPath), Times.Once);
    }

    [Test]
    public async Task DeleteLocalBranchAsync_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "branch not found" });

        // Act
        var result = await _service.DeleteLocalBranchAsync(repoPath, "non-existent");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region RemoteBranchExistsAsync Tests

    [Test]
    public async Task RemoteBranchExistsAsync_BranchExists_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"feature/exists\"", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "abc123def456\trefs/heads/feature/exists"
            });

        // Act
        var result = await _service.RemoteBranchExistsAsync(repoPath, "feature/exists");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RemoteBranchExistsAsync_BranchDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"feature/nonexistent\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.RemoteBranchExistsAsync(repoPath, "feature/nonexistent");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RemoteBranchExistsAsync_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"feature/test\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "network error" });

        // Act
        var result = await _service.RemoteBranchExistsAsync(repoPath, "feature/test");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region DeleteRemoteBranchAsync Tests

    [Test]
    public async Task DeleteRemoteBranchAsync_RemoteBranchDoesNotExist_ReturnsTrueWithoutDeleting()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        // Remote branch doesn't exist
        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"feature/not-pushed\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.DeleteRemoteBranchAsync(repoPath, "feature/not-pushed");

        // Assert
        Assert.That(result, Is.True);
        // Verify that push --delete was NOT called
        _mockRunner.Verify(
            r => r.RunAsync("git", It.Is<string>(s => s.Contains("push origin --delete")), repoPath),
            Times.Never);
    }

    [Test]
    public async Task DeleteRemoteBranchAsync_RemoteBranchExists_DeletesAndReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        // Remote branch exists
        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"feature/remote\"", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "abc123\trefs/heads/feature/remote"
            });

        // Delete succeeds
        _mockRunner.Setup(r => r.RunAsync("git", "push origin --delete \"feature/remote\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DeleteRemoteBranchAsync(repoPath, "feature/remote");

        // Assert
        Assert.That(result, Is.True);
        _mockRunner.Verify(
            r => r.RunAsync("git", "push origin --delete \"feature/remote\"", repoPath),
            Times.Once);
    }

    [Test]
    public async Task DeleteRemoteBranchAsync_DeleteFails_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        // Remote branch exists
        _mockRunner.Setup(r => r.RunAsync("git", "ls-remote --heads origin \"protected\"", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "abc123\trefs/heads/protected"
            });

        // Delete fails (e.g., protected branch)
        _mockRunner.Setup(r => r.RunAsync("git", "push origin --delete \"protected\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "remote rejected (protected branch)" });

        // Act
        var result = await _service.DeleteRemoteBranchAsync(repoPath, "protected");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region CreateLocalBranchFromRemoteAsync Tests

    [Test]
    public async Task CreateLocalBranchFromRemoteAsync_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        // The branch name is used as-is, without extracting parts

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -b \"feature/from-remote\" \"origin/feature/from-remote\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateLocalBranchFromRemoteAsync(repoPath, "feature/from-remote");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task CreateLocalBranchFromRemoteAsync_CheckoutFails_FallsBackToBranchCommand()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -b \"feature/test\" \"origin/feature/test\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "cannot checkout" });

        _mockRunner.Setup(r => r.RunAsync("git", "branch \"feature/test\" \"origin/feature/test\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateLocalBranchFromRemoteAsync(repoPath, "feature/test");

        // Assert
        Assert.That(result, Is.True);
        _mockRunner.Verify(r => r.RunAsync("git", "branch \"feature/test\" \"origin/feature/test\"", repoPath), Times.Once);
    }

    [Test]
    public async Task CreateLocalBranchFromRemoteAsync_BothCommandsFail_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -b \"feature/fail\" \"origin/feature/fail\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        _mockRunner.Setup(r => r.RunAsync("git", "branch \"feature/fail\" \"origin/feature/fail\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var result = await _service.CreateLocalBranchFromRemoteAsync(repoPath, "feature/fail");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetBranchDivergenceAsync Tests

    [Test]
    public async Task GetBranchDivergenceAsync_Success_ReturnsCorrectCounts()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count \"main...feature/test\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "3\t5" });

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(repoPath, "feature/test", "main");

        // Assert
        Assert.That(ahead, Is.EqualTo(5));
        Assert.That(behind, Is.EqualTo(3));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_NoDivergence_ReturnsZeros()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t0" });

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(repoPath, "feature/synced", "main");

        // Assert
        Assert.That(ahead, Is.EqualTo(0));
        Assert.That(behind, Is.EqualTo(0));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_GitError_ReturnsZeros()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", It.IsAny<string>(), repoPath))
            .ReturnsAsync(new CommandResult { Success = false });

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(repoPath, "feature/test", "main");

        // Assert
        Assert.That(ahead, Is.EqualTo(0));
        Assert.That(behind, Is.EqualTo(0));
    }

    #endregion

    #region FetchAllAsync Tests

    [Test]
    public async Task FetchAllAsync_Success_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "fetch --all --prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.FetchAllAsync(repoPath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task FetchAllAsync_GitError_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");

        _mockRunner.Setup(r => r.RunAsync("git", "fetch --all --prune", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "network error" });

        // Act
        var result = await _service.FetchAllAsync(repoPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetWorktreeStatusAsync Tests

    [Test]
    public async Task GetWorktreeStatusAsync_CleanWorktree_ReturnsZeroCounts()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetWorktreeStatusAsync(worktreePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(0));
        Assert.That(result.StagedCount, Is.EqualTo(0));
        Assert.That(result.UntrackedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetWorktreeStatusAsync_ModifiedFiles_ReturnsCorrectCount()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", worktreePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = " M src/file1.cs\n M src/file2.cs\n M src/file3.cs"
            });

        // Act
        var result = await _service.GetWorktreeStatusAsync(worktreePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(3));
    }

    [Test]
    public async Task GetWorktreeStatusAsync_MixedStatus_ReturnsCorrectCounts()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", worktreePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "M  src/staged.cs\n M src/modified.cs\nMM src/both.cs\n?? src/untracked.cs\nA  src/added.cs"
            });

        // Act
        var result = await _service.GetWorktreeStatusAsync(worktreePath);

        // Assert
        Assert.That(result.StagedCount, Is.EqualTo(3)); // M, MM, A in first position
        Assert.That(result.ModifiedCount, Is.EqualTo(2)); // M, MM in second position
        Assert.That(result.UntrackedCount, Is.EqualTo(1)); // ??
    }

    [Test]
    public async Task GetWorktreeStatusAsync_GitError_ReturnsEmptyStatus()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", worktreePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.GetWorktreeStatusAsync(worktreePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(0));
        Assert.That(result.StagedCount, Is.EqualTo(0));
        Assert.That(result.UntrackedCount, Is.EqualTo(0));
    }

    #endregion

    #region FindLostWorktreeFoldersAsync Tests

    [Test]
    public async Task FindLostWorktreeFoldersAsync_NoLostFolders_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Only the main folder exists
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {repoPath}\nbranch refs/heads/main"
            });

        // Act
        var result = await _service.FindLostWorktreeFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FindLostWorktreeFoldersAsync_WithLostFolders_ReturnsLostPaths()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a sibling folder that's not tracked by git worktree
        // It must have a .git file or folder to be recognized as a potential worktree
        var lostFolder = Path.Combine(_tempDir, "feature-abandoned");
        Directory.CreateDirectory(lostFolder);
        File.WriteAllText(Path.Combine(lostFolder, ".git"), "gitdir: /some/path");

        // Only the main worktree is tracked
        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {repoPath}\nbranch refs/heads/main"
            });

        // Act
        var result = await _service.FindLostWorktreeFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo(lostFolder));
    }

    [Test]
    public async Task FindLostWorktreeFoldersAsync_IgnoresNonGitFolders_ReturnsOnlyGitFolders()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a sibling folder that looks like a worktree (has .git)
        var lostFolder = Path.Combine(_tempDir, "feature-lost");
        Directory.CreateDirectory(lostFolder);
        File.WriteAllText(Path.Combine(lostFolder, ".git"), "gitdir: /some/path");

        // Create a non-git folder
        var regularFolder = Path.Combine(_tempDir, "regular-folder");
        Directory.CreateDirectory(regularFolder);

        _mockRunner.Setup(r => r.RunAsync("git", "worktree list --porcelain", repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"worktree {repoPath}\nbranch refs/heads/main"
            });

        // Act
        var result = await _service.FindLostWorktreeFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo(lostFolder));
    }

    #endregion

    #region DeleteWorktreeFolderAsync Tests

    [Test]
    public async Task DeleteWorktreeFolderAsync_FolderExists_DeletesFolder()
    {
        // Arrange
        var folderPath = Path.Combine(_tempDir, "folder-to-delete");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        // Act
        var result = await _service.DeleteWorktreeFolderAsync(folderPath);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(folderPath), Is.False);
    }

    [Test]
    public async Task DeleteWorktreeFolderAsync_FolderDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var folderPath = Path.Combine(_tempDir, "nonexistent-folder");

        // Act
        var result = await _service.DeleteWorktreeFolderAsync(folderPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetCurrentBranchAsync Tests

    [Test]
    public async Task GetCurrentBranchAsync_Success_ReturnsBranchName()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature/test-branch" });

        // Act
        var result = await _service.GetCurrentBranchAsync(worktreePath);

        // Assert
        Assert.That(result, Is.EqualTo("feature/test-branch"));
    }

    [Test]
    public async Task GetCurrentBranchAsync_DetachedHead_ReturnsHEAD()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "HEAD" });

        // Act
        var result = await _service.GetCurrentBranchAsync(worktreePath);

        // Assert
        Assert.That(result, Is.EqualTo("HEAD"));
    }

    [Test]
    public async Task GetCurrentBranchAsync_GitError_ReturnsNull()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.GetCurrentBranchAsync(worktreePath);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region CheckoutBranchAsync Tests

    [Test]
    public async Task CheckoutBranchAsync_Success_ReturnsTrue()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CheckoutBranchAsync(worktreePath, branchName);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task CheckoutBranchAsync_BranchDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        var branchName = "nonexistent-branch";

        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", worktreePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "error: pathspec 'nonexistent-branch' did not match any file(s) known to git" });

        // Act
        var result = await _service.CheckoutBranchAsync(worktreePath, branchName);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region IsSquashMergedAsync Tests

    [Test]
    public async Task IsSquashMergedAsync_BranchIsSquashMerged_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var branchName = "feature/squashed";
        var targetBranch = "main";

        // Simulate: cherry-pick --no-commit finds no diff (all commits already in main)
        _mockRunner.Setup(r => r.RunAsync("git", $"log \"{targetBranch}..{branchName}\" --format=%H", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123\ndef456" });

        // Check if tree is equivalent after squash
        _mockRunner.Setup(r => r.RunAsync("git", $"cherry \"{targetBranch}\" \"{branchName}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" }); // Empty means all commits are equivalent

        // Act
        var result = await _service.IsSquashMergedAsync(repoPath, branchName, targetBranch);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsSquashMergedAsync_BranchNotSquashMerged_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var branchName = "feature/not-merged";
        var targetBranch = "main";

        _mockRunner.Setup(r => r.RunAsync("git", $"log \"{targetBranch}..{branchName}\" --format=%H", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Cherry shows commits that are not yet in target
        _mockRunner.Setup(r => r.RunAsync("git", $"cherry \"{targetBranch}\" \"{branchName}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "+ abc123" }); // + means not in target

        // Act
        var result = await _service.IsSquashMergedAsync(repoPath, branchName, targetBranch);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region CreateWorktreeFromRemoteBranchAsync Tests

    [Test]
    public async Task CreateWorktreeFromRemoteBranchAsync_Success_CreatesWorktreeWithoutCheckingOut()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var remoteBranch = "feature/remote-only";
        var expectedWorktreePath = Path.Combine(_tempDir, "feature/remote-only");

        // First, create the local branch without checkout (using git branch instead of checkout)
        _mockRunner.Setup(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Then create the worktree
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("worktree add")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateWorktreeFromRemoteBranchAsync(repoPath, remoteBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Verify git branch was called (not checkout which would change the main worktree)
        _mockRunner.Verify(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath), Times.Once);
    }

    [Test]
    public async Task CreateWorktreeFromRemoteBranchAsync_BranchAlreadyExists_UsesExistingBranch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var remoteBranch = "feature/existing";

        // Branch creation fails because it already exists
        _mockRunner.Setup(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: a branch named 'feature/existing' already exists" });

        // Worktree creation succeeds
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("worktree add")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateWorktreeFromRemoteBranchAsync(repoPath, remoteBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region GetChangedFilesAsync Tests

    [Test]
    public async Task GetChangedFilesAsync_WithChanges_ReturnsFileList()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // git diff --numstat output format: additions<tab>deletions<tab>filename
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "10\t5\tsrc/Components/Button.cs\n3\t0\tsrc/Services/MyService.cs\n0\t15\tsrc/Old/Deprecated.cs"
            });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].FilePath, Is.EqualTo("src/Components/Button.cs"));
        Assert.That(result[0].Additions, Is.EqualTo(10));
        Assert.That(result[0].Deletions, Is.EqualTo(5));
    }

    [Test]
    public async Task GetChangedFilesAsync_NoChanges_ReturnsEmptyList()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChangedFilesAsync_ParsesAdditions_Correctly()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "100\t0\tnew-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Additions, Is.EqualTo(100));
        Assert.That(result[0].Deletions, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Added));
    }

    [Test]
    public async Task GetChangedFilesAsync_ParsesDeletions_Correctly()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t50\tdeleted-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Additions, Is.EqualTo(0));
        Assert.That(result[0].Deletions, Is.EqualTo(50));
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Deleted));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsAddedFiles()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // A file with only additions and no deletions is a new file
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "25\t0\tnew-feature.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Added));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsDeletedFiles()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // A file with only deletions and no additions is a deleted file
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t30\told-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Deleted));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsModifiedFiles()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // A file with both additions and deletions is a modified file
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "10\t5\tmodified-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsRenamedFiles()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // Renamed files show as "old-name => new-name" in the path
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t0\told-name.cs => new-name.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Renamed));
        Assert.That(result[0].FilePath, Does.Contain("=>"));
    }

    [Test]
    public async Task GetChangedFilesAsync_GitError_ReturnsEmptyList()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: not a git repository" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChangedFilesAsync_BinaryFiles_HandlesCorrectly()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        // Binary files show "-" for additions and deletions
        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "-\t-\timage.png" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FilePath, Is.EqualTo("image.png"));
        Assert.That(result[0].Additions, Is.EqualTo(0)); // Binary files have 0 for line counts
        Assert.That(result[0].Deletions, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public async Task GetChangedFilesAsync_PathsWithSpaces_HandlesCorrectly()
    {
        // Arrange
        var worktreePath = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(worktreePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", worktreePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "5\t3\tsrc/My Components/Button Component.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(worktreePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FilePath, Is.EqualTo("src/My Components/Button Component.cs"));
    }

    #endregion
}
