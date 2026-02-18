using Homespun.Features.Testing;
using Homespun.Tests.Helpers;

namespace Homespun.Tests.Features.Git;

/// <summary>
/// Integration tests for GitCloneService that test against real git repositories.
/// These tests require git to be installed and available on the PATH.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GitCloneServiceIntegrationTests
{
    private TempGitRepositoryFixture _fixture = null!;
    private GitCloneService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TempGitRepositoryFixture();
        _service = new GitCloneService(); // Uses real CommandRunner
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
    }

    /// <summary>
    /// Normalizes a path for comparison (git returns forward slashes on Windows).
    /// </summary>
    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    [Test]
    public async Task CreateClone_WithExistingBranch_CreatesCloneSuccessfully()
    {
        // Arrange
        var branchName = "feature/test-clone";
        _fixture.CreateBranch(branchName);

        // Act
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(clonePath, Does.EndWith("workdir"), "CreateCloneAsync should return the workdir path");
        Assert.That(Directory.Exists(clonePath), Is.True);
        // Git repo is now the returned workdir path
        Assert.That(File.Exists(Path.Combine(clonePath!, "README.md")), Is.True);
        // Verify .claude directory was created as sibling of workdir
        var cloneRoot = Path.GetDirectoryName(clonePath!);
        Assert.That(Directory.Exists(Path.Combine(cloneRoot!, ".claude")), Is.True);
    }

    [Test]
    public async Task CreateClone_WithNewBranch_CreatesBranchAndClone()
    {
        // Arrange
        var branchName = "feature/new-branch";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(Directory.Exists(clonePath), Is.True);

        // Verify the branch was created
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain(branchName));
    }

    [Test]
    public async Task CreateClone_WithBaseBranch_CreatesBranchFromSpecifiedBase()
    {
        // Arrange
        var baseBranch = "develop";
        var featureBranch = "feature/from-develop";

        // Create develop branch with additional content
        _fixture.CreateBranch(baseBranch, checkout: true);
        _fixture.CreateFileAndCommit("develop.txt", "Develop content", "Add develop file");
        _fixture.RunGit("checkout -"); // Go back to main/master

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            featureBranch,
            createBranch: true,
            baseBranch: baseBranch);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(clonePath, Does.EndWith("workdir"), "CreateCloneAsync should return the workdir path");
        Assert.That(Directory.Exists(clonePath), Is.True);
        // The clone should have the file from develop branch (workdir is returned directly)
        Assert.That(File.Exists(Path.Combine(clonePath!, "develop.txt")), Is.True);
    }

    [Test]
    public async Task CreateClone_NonExistentBranch_ReturnsNull()
    {
        // Arrange
        var branchName = "non-existent-branch";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: false);

        // Assert
        Assert.That(clonePath, Is.Null);
    }

    [Test]
    public async Task ListClones_ReturnsMainClone()
    {
        // Act
        var clones = await _service.ListClonesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(clones, Is.Not.Null);
        Assert.That(clones, Has.Count.EqualTo(1));
        Assert.That(NormalizePath(clones[0].Path), Is.EqualTo(NormalizePath(_fixture.RepositoryPath)));
    }

    [Test]
    public async Task ListClones_AfterCreatingClone_ReturnsMultipleClones()
    {
        // Arrange
        var branchName = "feature/list-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);

        // Act
        var clones = await _service.ListClonesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(clones, Is.Not.Null);
        Assert.That(clones, Has.Count.EqualTo(2));
        Assert.That(clones, Has.Some.Matches<CloneInfo>(w => NormalizePath(w.Path) == NormalizePath(_fixture.RepositoryPath)));
        Assert.That(clones, Has.Some.Matches<CloneInfo>(w => w.Branch?.EndsWith(branchName) == true));
    }

    [Test]
    public async Task ListClones_ReturnsCorrectBranchInfo()
    {
        // Arrange
        var branchName = "feature/branch-info-test";
        _fixture.CreateBranch(branchName);
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Act
        var clones = await _service.ListClonesAsync(_fixture.RepositoryPath);

        // Assert - clonePath is now the workdir path, so compare against WorkdirPath
        var clone = clones.FirstOrDefault(w => w.WorkdirPath != null && NormalizePath(w.WorkdirPath) == NormalizePath(clonePath!));
        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Branch, Does.EndWith(branchName));
        Assert.That(clone.HeadCommit, Is.Not.Null);
        Assert.That(clone.IsDetached, Is.False);
        Assert.That(clone.WorkdirPath, Is.EqualTo(clonePath));
    }

    [Test]
    public async Task RemoveClone_ExistingClone_RemovesSuccessfully()
    {
        // Arrange
        var branchName = "feature/remove-test";
        _fixture.CreateBranch(branchName);
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(clonePath, Does.EndWith("workdir"), "CreateCloneAsync should return workdir path");
        Assert.That(Directory.Exists(clonePath), Is.True);

        // Act - RemoveCloneAsync handles workdir path (derives clone root from it)
        var result = await _service.RemoveCloneAsync(_fixture.RepositoryPath, clonePath!);

        // Assert - both workdir and its parent (clone root) should be removed
        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(clonePath), Is.False);
        var cloneRoot = Path.GetDirectoryName(clonePath!);
        Assert.That(Directory.Exists(cloneRoot), Is.False);
    }

    [Test]
    public async Task RemoveClone_NonExistentClone_ReturnsFalse()
    {
        // Arrange
        var fakeClonePath = Path.Combine(_fixture.RepositoryPath, ".clones", "non-existent");

        // Act
        var result = await _service.RemoveCloneAsync(_fixture.RepositoryPath, fakeClonePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CloneExists_ExistingClone_ReturnsTrue()
    {
        // Arrange
        var branchName = "feature/exists-test";
        _fixture.CreateBranch(branchName);
        await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);

        // Act
        var exists = await _service.CloneExistsAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task CloneExists_NonExistentClone_ReturnsFalse()
    {
        // Act
        var exists = await _service.CloneExistsAsync(_fixture.RepositoryPath, "non-existent-branch");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task PruneClones_RemovesStaleCloneReferences()
    {
        // Arrange
        var branchName = "feature/prune-test";
        _fixture.CreateBranch(branchName);
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Manually delete the clone directory (simulating stale clone)
        if (Directory.Exists(clonePath))
        {
            foreach (var file in Directory.GetFiles(clonePath!, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(clonePath!, recursive: true);
        }

        // Act - should not throw
        await _service.PruneClonesAsync(_fixture.RepositoryPath);

        // Assert - clone should be pruned from the list
        var clones = await _service.ListClonesAsync(_fixture.RepositoryPath);
        Assert.That(clones, Has.None.Matches<CloneInfo>(w => NormalizePath(w.Path) == NormalizePath(clonePath!)));
    }

    [Test]
    public async Task CreateClone_WithModifiedFiles_CloneHasCleanState()
    {
        // Arrange
        var branchName = "feature/clean-state-test";
        _fixture.CreateBranch(branchName);

        // Modify a file in the main clone (uncommitted change)
        var readmePath = Path.Combine(_fixture.RepositoryPath, "README.md");
        File.AppendAllText(readmePath, "\n\nModified content.");

        // Act
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(clonePath, Does.EndWith("workdir"), "CreateCloneAsync should return workdir path");

        // The new clone should have the clean version from the branch (workdir is returned directly)
        var cloneReadme = File.ReadAllText(Path.Combine(clonePath!, "README.md"));
        Assert.That(cloneReadme, Does.Not.Contain("Modified content."));
    }

    [Test]
    public async Task CreateClone_BranchNameWithSpecialCharacters_SanitizesPath()
    {
        // Arrange
        var branchName = "feature/special@chars#test";
        _fixture.RunGit($"branch \"{branchName}\"");

        // Act
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(clonePath, Does.Not.Contain("@"));
        Assert.That(clonePath, Does.Not.Contain("#"));
        Assert.That(Directory.Exists(clonePath), Is.True);
    }

    #region GetClonePathForBranchAsync Integration Tests

    /// <summary>
    /// Integration test: Clone path with + character should be preserved in the flat structure.
    /// The new flat structure uses + as a separator between type, branch-id, and issue-id.
    /// </summary>
    [Test]
    public async Task GetClonePathForBranch_WithPlusInBranchName_PreservesPlusInClonePath()
    {
        // Arrange
        // The branch name as it would appear in the new flat format: type/name+issueId
        var branchName = "feature/improve-tool-output+aLP3LH";

        // Create the branch in git (git allows + in branch names)
        _fixture.RunGit($"branch \"{branchName}\"");

        // Create a clone for this branch - path will be flat with + preserved
        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Verify the clone is in .clones directory with flat name
        Assert.That(clonePath, Does.Contain(".clones"));
        // + is preserved, slashes become +
        Assert.That(clonePath, Does.Contain("feature+improve-tool-output+aLP3LH"));

        // Act - Now simulate what happens during PR sync:
        // Given only the branch name (with +), can we find the existing clone?
        var foundPath = await _service.GetClonePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null, "Should find the clone path for the branch with + character");
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(clonePath!)));
    }

    [Test]
    public async Task GetClonePathForBranch_WithNoExistingClone_ReturnsNull()
    {
        // Arrange
        var branchName = "feature/no-clone+test";
        _fixture.RunGit($"branch \"{branchName}\"");
        // Note: We do NOT create a clone for this branch

        // Act
        var foundPath = await _service.GetClonePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Null);
    }

    [Test]
    public async Task GetClonePathForBranch_WithRegularBranchName_MatchesDirectly()
    {
        // Arrange
        var branchName = "feature/normal-branch";
        _fixture.CreateBranch(branchName);

        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Act
        var foundPath = await _service.GetClonePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(clonePath!)));
    }

    [Test]
    public async Task GetClonePathForBranch_WithMultipleSpecialCharacters_MatchesFlattenedPath()
    {
        // Arrange - Test with special characters that get sanitized
        // Note: + is preserved, but @ and # become dashes
        var branchName = "feature/test+foo@bar#baz";
        _fixture.RunGit($"branch \"{branchName}\"");

        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Verify sanitization happened - + is preserved, @ and # become -
        Assert.That(clonePath, Does.Contain("feature+test+foo-bar-baz"));

        // Act
        var foundPath = await _service.GetClonePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(clonePath!)));
    }

    #endregion

    #region ListLocalBranchesAsync Integration Tests

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsMainBranch()
    {
        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(branches, Is.Not.Null);
        Assert.That(branches, Has.Count.GreaterThanOrEqualTo(1));

        // Should have either 'main' or 'master' depending on git config
        var mainBranch = branches.FirstOrDefault(b => b.ShortName == "main" || b.ShortName == "master");
        Assert.That(mainBranch, Is.Not.Null);
        Assert.That(mainBranch!.IsCurrent, Is.True);
    }

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsMultipleBranches()
    {
        // Arrange
        _fixture.CreateBranch("feature/test-1");
        _fixture.CreateBranch("feature/test-2");

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        Assert.That(branches, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(branches, Has.Some.Matches<BranchInfo>(b => b.ShortName == "feature/test-1"));
        Assert.That(branches, Has.Some.Matches<BranchInfo>(b => b.ShortName == "feature/test-2"));
    }

    [Test]
    public async Task ListLocalBranchesAsync_IdentifiesCloneBranches()
    {
        // Arrange
        var branchName = "feature/clone-branch";
        _fixture.CreateBranch(branchName);

        var clonePath = await _service.CreateCloneAsync(_fixture.RepositoryPath, branchName);
        Assert.That(clonePath, Is.Not.Null);

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        var cloneBranch = branches.FirstOrDefault(b => b.ShortName == branchName);
        Assert.That(cloneBranch, Is.Not.Null);
        Assert.That(cloneBranch!.HasClone, Is.True);
        Assert.That(cloneBranch.ClonePath, Is.Not.Null);
    }

    [Test]
    public async Task ListLocalBranchesAsync_ReturnsCommitInfo()
    {
        // Arrange
        _fixture.CreateBranch("feature/with-commit", checkout: true);
        _fixture.CreateFileAndCommit("test.txt", "test content", "Add test file");
        _fixture.RunGit("checkout -"); // Go back to main

        // Act
        var branches = await _service.ListLocalBranchesAsync(_fixture.RepositoryPath);

        // Assert
        var featureBranch = branches.FirstOrDefault(b => b.ShortName == "feature/with-commit");
        Assert.That(featureBranch, Is.Not.Null);
        Assert.That(featureBranch!.CommitSha, Is.Not.Null.And.Not.Empty);
        Assert.That(featureBranch.LastCommitMessage, Does.Contain("Add test file"));
    }

    #endregion

    #region IsBranchMergedAsync Integration Tests

    [Test]
    public async Task IsBranchMergedAsync_MergedBranch_ReturnsTrue()
    {
        // Arrange - Create and merge a branch
        var branchName = "feature/to-merge";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("merged.txt", "merged content", "Add merged file");
        _fixture.RunGit("checkout -"); // Back to main
        _fixture.RunGit($"merge \"{branchName}\" --no-ff -m \"Merge feature branch\"");

        // Get the default branch name
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        // Act
        var isMerged = await _service.IsBranchMergedAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(isMerged, Is.True);
    }

    [Test]
    public async Task IsBranchMergedAsync_UnmergedBranch_ReturnsFalse()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/unmerged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("unmerged.txt", "unmerged content", "Add unmerged file");
        _fixture.RunGit("checkout -"); // Back to main

        // Get the default branch name
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        // Act
        var isMerged = await _service.IsBranchMergedAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(isMerged, Is.False);
    }

    #endregion

    #region DeleteLocalBranchAsync Integration Tests

    [Test]
    public async Task DeleteLocalBranchAsync_MergedBranch_DeletesSuccessfully()
    {
        // Arrange - Create and merge a branch
        var branchName = "feature/delete-merged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("delete.txt", "content", "Add file");
        _fixture.RunGit("checkout -");
        _fixture.RunGit($"merge \"{branchName}\" --no-ff -m \"Merge\"");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(result, Is.True);

        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Not.Contain(branchName));
    }

    [Test]
    public async Task DeleteLocalBranchAsync_UnmergedBranch_FailsWithoutForce()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/delete-unmerged";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("unmerged.txt", "content", "Add file");
        _fixture.RunGit("checkout -");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName, force: false);

        // Assert
        Assert.That(result, Is.False);

        // Branch should still exist
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain(branchName));
    }

    [Test]
    public async Task DeleteLocalBranchAsync_UnmergedBranchWithForce_DeletesSuccessfully()
    {
        // Arrange - Create a branch with changes but don't merge
        var branchName = "feature/force-delete";
        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("force.txt", "content", "Add file");
        _fixture.RunGit("checkout -");

        // Act
        var result = await _service.DeleteLocalBranchAsync(_fixture.RepositoryPath, branchName, force: true);

        // Assert
        Assert.That(result, Is.True);

        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Not.Contain(branchName));
    }

    #endregion

    #region GetBranchDivergenceAsync Integration Tests

    [Test]
    public async Task GetBranchDivergenceAsync_BranchAhead_ReturnsCorrectCount()
    {
        // Arrange
        var branchName = "feature/ahead-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("file1.txt", "content1", "Commit 1");
        _fixture.CreateFileAndCommit("file2.txt", "content2", "Commit 2");
        _fixture.RunGit("checkout -");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(2));
        Assert.That(behind, Is.EqualTo(0));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_BranchBehind_ReturnsCorrectCount()
    {
        // Arrange
        var branchName = "feature/behind-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName);
        // Add commits to main after creating branch
        _fixture.CreateFileAndCommit("main1.txt", "main content", "Main commit 1");
        _fixture.CreateFileAndCommit("main2.txt", "main content 2", "Main commit 2");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(0));
        Assert.That(behind, Is.EqualTo(2));
    }

    [Test]
    public async Task GetBranchDivergenceAsync_BranchDiverged_ReturnsBothCounts()
    {
        // Arrange
        var branchName = "feature/diverged-branch";
        var defaultBranch = _fixture.RunGit("rev-parse --abbrev-ref HEAD").Trim();

        _fixture.CreateBranch(branchName, checkout: true);
        _fixture.CreateFileAndCommit("feature.txt", "feature content", "Feature commit");
        _fixture.RunGit("checkout -");
        _fixture.CreateFileAndCommit("main.txt", "main content", "Main commit");

        // Act
        var (ahead, behind) = await _service.GetBranchDivergenceAsync(_fixture.RepositoryPath, branchName, defaultBranch);

        // Assert
        Assert.That(ahead, Is.EqualTo(1));
        Assert.That(behind, Is.EqualTo(1));
    }

    #endregion

    #region FetchAllAsync Integration Tests

    [Test]
    public async Task FetchAllAsync_NoRemote_DoesNotThrow()
    {
        // The test repo has no remotes configured
        // git fetch --all --prune might succeed (returning exit 0) or fail depending on git version
        // The important thing is it doesn't throw an exception

        // Act & Assert - Should not throw
        await _service.FetchAllAsync(_fixture.RepositoryPath);
    }

    #endregion

    #region Branch Name Recalculation Tests (Issue 1JudQJ)

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that when creating a clone,
    /// the branch name is calculated from current issue properties (type, title).
    /// This test simulates the scenario where an issue's type changes before creating a clone.
    /// </summary>
    [Test]
    public async Task CreateClone_WithRecalculatedBranchName_CreatesCorrectBranch()
    {
        // Arrange - Simulate the branch naming pattern used by Homespun
        // New flat format: {type}/{branch-id}+{issue-id}
        var issueId = "abc123";

        // Original branch name (as if issue was Feature type)
        var originalBranchName = $"feature/fix-something+{issueId}";

        // Recalculated branch name (as if issue was changed to Bug type)
        var recalculatedBranchName = $"bug/fix-something+{issueId}";

        // Act - Create clone with the recalculated branch name
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            recalculatedBranchName,
            createBranch: true);

        // Assert - Clone should be created with the recalculated name
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(Directory.Exists(clonePath), Is.True);

        // Verify the branch was created with the correct name
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain("bug/fix-something+abc123"));
        Assert.That(branches, Does.Not.Contain("feature/fix-something+abc123"));
    }

    /// <summary>
    /// Integration test: Verifies that the clone path uses the flat structure
    /// with the .clones directory and flattened branch name.
    /// </summary>
    [Test]
    public async Task CreateClone_UsesFlatCloneStructure()
    {
        // Arrange - Branch name in new flat format
        var branchName = "task/implement-feature+xyz789";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert
        Assert.That(clonePath, Is.Not.Null);
        Assert.That(Directory.Exists(clonePath), Is.True);

        // Verify the clone is in .clones directory with flat name
        Assert.That(clonePath, Does.Contain(".clones"));
        Assert.That(clonePath, Does.Contain("task+implement-feature+xyz789"));

        // Verify the branch was created correctly
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain("task/implement-feature+xyz789"));
    }

    /// <summary>
    /// Integration test: Verifies that the clone path matches
    /// the branch name (after flattening) to ensure consistency.
    /// </summary>
    [Test]
    public async Task CreateClone_BranchNameAndClonePath_AreConsistent()
    {
        // Arrange - New flat format
        var branchName = "feature/some-feature+def456";

        // Act
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);

        // Assert - Clone path should be in .clones with flattened name
        Assert.That(clonePath, Is.Not.Null);

        // The clone folder name should be flattened (/ -> +, existing + preserved)
        Assert.That(clonePath, Does.Contain(".clones"));
        Assert.That(clonePath, Does.Contain("feature+some-feature+def456"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that creating a clone with a different
    /// branch name (simulating an issue property change) creates a separate clone.
    /// This demonstrates that old clones are NOT automatically updated.
    /// </summary>
    [Test]
    public async Task CreateClone_WithDifferentBranchNames_CreatesSeparateClones()
    {
        // Arrange - Two different branch names for same logical issue
        // (simulating issue type change from Feature to Bug)
        var issueId = "conflict1";
        var originalBranchName = $"feature/test-issue+{issueId}";
        var modifiedBranchName = $"bug/test-issue+{issueId}";

        // Act - Create clones with both names
        var clone1 = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            originalBranchName,
            createBranch: true);

        var clone2 = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            modifiedBranchName,
            createBranch: true);

        // Assert - Both clones should exist (this is the current behavior)
        // The fix ensures the UI always uses the recalculated name, preventing this scenario
        Assert.That(clone1, Is.Not.Null);
        Assert.That(clone2, Is.Not.Null);
        Assert.That(clone1, Is.Not.EqualTo(clone2));

        // Both branches should exist
        var branches = _fixture.RunGit("branch --list");
        Assert.That(branches, Does.Contain($"feature/test-issue+{issueId}"));
        Assert.That(branches, Does.Contain($"bug/test-issue+{issueId}"));
    }

    /// <summary>
    /// Integration test for issue 1JudQJ: Verifies that GetClonePathForBranch
    /// can find a clone using the recalculated branch name.
    /// </summary>
    [Test]
    public async Task GetClonePathForBranch_WithRecalculatedName_FindsCorrectClone()
    {
        // Arrange - Create a clone with initial branch name (new flat format)
        var branchName = "task/my-task+recalc1";
        var clonePath = await _service.CreateCloneAsync(
            _fixture.RepositoryPath,
            branchName,
            createBranch: true);
        Assert.That(clonePath, Is.Not.Null);

        // Act - Look up the clone using the same branch name
        // (simulating that recalculation produces the same name)
        var foundPath = await _service.GetClonePathForBranchAsync(_fixture.RepositoryPath, branchName);

        // Assert
        Assert.That(foundPath, Is.Not.Null);
        Assert.That(NormalizePath(foundPath!), Is.EqualTo(NormalizePath(clonePath!)));
    }

    #endregion
}
