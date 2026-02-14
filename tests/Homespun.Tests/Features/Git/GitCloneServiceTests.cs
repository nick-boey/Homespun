using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class GitCloneServiceTests
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
    public async Task CreateClone_Success_ReturnsPath()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        // Mock remote get-url (returns a remote URL)
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local - now clones into workdir subdirectory
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url in the clone directory (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock checkout in the clone directory (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Clone should be in .clones directory with flattened name (/ becomes +)
        Assert.That(result, Does.Contain(".clones"));
        Assert.That(result, Does.Contain("feature+test"));
        // Verify .claude directory was created
        var claudeDir = Path.Combine(result!, ".claude");
        Assert.That(Directory.Exists(claudeDir), Is.True, "Expected .claude directory to be created");
    }

    [Test]
    public async Task CreateClone_CloneError_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local failure
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: clone failed" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveClone_DirectoryExists_DeletesAndReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var clonePath = Path.Combine(repoPath, ".clones", "feature-test");
        Directory.CreateDirectory(clonePath);

        // Act
        var result = await _service.RemoveCloneAsync(repoPath, clonePath);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(clonePath), Is.False);
    }

    [Test]
    public async Task RemoveClone_DirectoryDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        var clonePath = Path.Combine(repoPath, ".clones", "feature-test");

        // Act
        var result = await _service.RemoveCloneAsync(repoPath, clonePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ListClones_WithClones_ReturnsClones()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create .clones directory with two clone directories containing .git markers
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clone1 = Path.Combine(clonesDir, "feature-1");
        var clone2 = Path.Combine(clonesDir, "feature-2");
        Directory.CreateDirectory(clone1);
        Directory.CreateDirectory(clone2);
        Directory.CreateDirectory(Path.Combine(clone1, ".git"));
        Directory.CreateDirectory(Path.Combine(clone2, ".git"));

        // Mock main repo branch/commit
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone 1
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clone1))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature-1" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clone1))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock clone 2
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clone2))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature-2" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clone2))
            .ReturnsAsync(new CommandResult { Success = true, Output = "ghi789" });

        // Mock for-each-ref (needed by ListClonesAsync -> ListLocalBranchesAsync)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3)); // main + 2 clones
    }

    [Test]
    public async Task ListClones_NoClones_ReturnsOnlyMainRepo()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock for-each-ref (needed by ListClonesAsync -> ListLocalBranchesAsync)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PruneClones_RemovesBrokenClones()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create .clones directory with a broken clone (no .git)
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var brokenClone = Path.Combine(clonesDir, "broken-clone");
        Directory.CreateDirectory(brokenClone);
        // No .git directory - this is a broken clone

        // Create a valid clone (has .git)
        var validClone = Path.Combine(clonesDir, "valid-clone");
        Directory.CreateDirectory(validClone);
        Directory.CreateDirectory(Path.Combine(validClone, ".git"));

        // Act
        await _service.PruneClonesAsync(repoPath);

        // Assert
        Assert.That(Directory.Exists(brokenClone), Is.False); // Broken clone deleted
        Assert.That(Directory.Exists(validClone), Is.True); // Valid clone preserved
    }

    [Test]
    public void SanitizeBranchName_PreservesSlashes()
    {
        // Act
        var result = GitCloneService.SanitizeBranchName("feature/new-thing");

        // Assert - slashes are preserved for git branch names
        Assert.That(result, Is.EqualTo("feature/new-thing"));
    }

    [Test]
    public void SanitizeBranchName_RemovesSpecialCharactersButPreservesSlashesAndPlus()
    {
        // Act
        var result = GitCloneService.SanitizeBranchName("feature/test@branch#1");

        // Assert - slashes preserved, special chars replaced with dashes
        Assert.That(result, Is.EqualTo("feature/test-branch-1"));
    }

    [Test]
    public void SanitizeBranchName_NormalizesBackslashesToForwardSlashes()
    {
        // Act
        var result = GitCloneService.SanitizeBranchName("app\\feature\\test");

        // Assert - backslashes converted to forward slashes
        Assert.That(result, Is.EqualTo("app/feature/test"));
    }

    [Test]
    public void SanitizeBranchName_TrimsSlashesFromEnds()
    {
        // Act
        var result = GitCloneService.SanitizeBranchName("/feature/test/");

        // Assert - leading/trailing slashes removed
        Assert.That(result, Is.EqualTo("feature/test"));
    }

    [Test]
    public void SanitizeBranchName_WithPlusCharacter_PreservesPlus()
    {
        // Act - Plus character is used to separate branch name from issue ID
        var result = GitCloneService.SanitizeBranchName("feature/improve-tool-output+aLP3LH");

        // Assert - Plus should be preserved for branch name matching
        Assert.That(result, Is.EqualTo("feature/improve-tool-output+aLP3LH"));
    }

    #region SanitizeBranchNameForClone Tests

    [Test]
    public void SanitizeBranchNameForClone_ConvertsSlashesToPlus()
    {
        // Act
        var result = GitCloneService.SanitizeBranchNameForClone("feature/new-thing");

        // Assert - slashes converted to plus for flat folder structure
        Assert.That(result, Is.EqualTo("feature+new-thing"));
    }

    [Test]
    public void SanitizeBranchNameForClone_PreservesPlus()
    {
        // Act - Full branch name with issue ID
        var result = GitCloneService.SanitizeBranchNameForClone("feature/improve-tool-output+aLP3LH");

        // Assert - Slashes become plus, existing plus is preserved
        Assert.That(result, Is.EqualTo("feature+improve-tool-output+aLP3LH"));
    }

    [Test]
    public void SanitizeBranchNameForClone_RemovesSpecialCharacters()
    {
        // Act
        var result = GitCloneService.SanitizeBranchNameForClone("feature/test@branch#1");

        // Assert - slashes become plus, special chars replaced with dashes
        Assert.That(result, Is.EqualTo("feature+test-branch-1"));
    }

    [Test]
    public void SanitizeBranchNameForClone_NormalizesBackslashesToPlus()
    {
        // Act
        var result = GitCloneService.SanitizeBranchNameForClone("app\\feature\\test");

        // Assert - backslashes converted to plus
        Assert.That(result, Is.EqualTo("app+feature+test"));
    }

    [Test]
    public void SanitizeBranchNameForClone_RemovesConsecutivePlus()
    {
        // Act
        var result = GitCloneService.SanitizeBranchNameForClone("feature//test");

        // Assert - consecutive slashes (now plus) are collapsed
        Assert.That(result, Is.EqualTo("feature+test"));
    }

    [Test]
    public void SanitizeBranchNameForClone_TrimsFromEnds()
    {
        // Act
        var result = GitCloneService.SanitizeBranchNameForClone("/feature/test/");

        // Assert - leading/trailing slashes (now plus) are trimmed
        Assert.That(result, Is.EqualTo("feature+test"));
    }

    #endregion

    [Test]
    public async Task CreateClone_WithNewBranch_CreatesBranchFirst()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/new-branch";

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock branch creation
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("branch ")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock clone --local - now clones into workdir subdirectory
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url in clone (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock checkout in clone (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("checkout")), It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName, createBranch: true);

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

    #region GetClonePathForBranchAsync Tests

    [Test]
    [Description("Clone path lookup should match branch via direct branch name")]
    public async Task GetClonePathForBranchAsync_WithRefsHeadsFormat_MatchesShortBranchName()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var shortBranchName = "feature/my-pr-branch";

        // Create clone directory with .git marker
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var expectedPath = Path.GetFullPath(Path.Combine(clonesDir, "feature+my-pr-branch"));
        Directory.CreateDirectory(expectedPath);
        Directory.CreateDirectory(Path.Combine(expectedPath, ".git"));

        // Mock main repo rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", expectedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = shortBranchName });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", expectedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref (needed by ListLocalBranchesAsync)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, shortBranchName);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    [Description("CloneExists should find clones by branch name")]
    public async Task CloneExistsAsync_WithClone_ReturnsTrue()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var shortBranchName = "feature/my-pr-branch";

        // Create clone directory with .git marker
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clonePath = Path.GetFullPath(Path.Combine(clonesDir, "feature+my-pr-branch"));
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = shortBranchName });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CloneExistsAsync(repoPath, shortBranchName);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GetClonePathForBranchAsync_WithDirectBranchMatch_ReturnsPath()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        var clonesDir = Path.Combine(_tempDir, ".clones");
        var expectedPath = Path.GetFullPath(Path.Combine(clonesDir, "feature+test"));
        Directory.CreateDirectory(expectedPath);
        Directory.CreateDirectory(Path.Combine(expectedPath, ".git"));

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", expectedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = branchName });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", expectedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    public async Task GetClonePathForBranchAsync_WithBranchNameContainingPlus_MatchesByBranch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/improve-tool-output+aLP3LH";

        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clonePath = Path.GetFullPath(Path.Combine(clonesDir, "feature+improve-tool-output+aLP3LH"));
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = branchName });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, branchName);

        // Assert - Should find by direct branch match
        Assert.That(result, Is.EqualTo(clonePath));
    }

    [Test]
    public async Task GetClonePathForBranchAsync_WithFlattenedPath_FallsBackToPathMatch()
    {
        // Arrange - Test the path-based fallback when branch doesn't match directly
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/improve-tool-output+aLP3LH";

        var clonesDir = Path.Combine(_tempDir, ".clones");
        var flattenedPath = Path.GetFullPath(Path.Combine(clonesDir, "feature+improve-tool-output+aLP3LH"));
        Directory.CreateDirectory(flattenedPath);
        Directory.CreateDirectory(Path.Combine(flattenedPath, ".git"));

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone - reports a different branch name (simulates a mismatch)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", flattenedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "some-other-branch" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", flattenedPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, branchName);

        // Assert - Should find by flattened path match
        Assert.That(result, Is.Not.Null);
        Assert.That(Path.GetFullPath(result!), Is.EqualTo(flattenedPath).IgnoreCase);
    }

    [Test]
    public async Task GetClonePathForBranchAsync_NoMatchingClone_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/nonexistent+test";

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetClonePathForBranchAsync_GitCommandFails_ReturnsNull()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test";

        // Mock main repo rev-parse failures
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "git error" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "git error" });

        // Mock for-each-ref failure
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "git error" });

        // Act
        var result = await _service.GetClonePathForBranchAsync(repoPath, branchName);

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

        // Mock main repo rev-parse (used by ListClonesRawAsync)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock for-each-ref for branches
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main|abc1234|origin/main|[ahead 1]|2024-01-15T10:30:00|Initial commit'\n'feature/test|def5678||[behind 2]|2024-01-16T11:00:00|Add feature'"
            });

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

        // Mock main repo rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.ListLocalBranchesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListLocalBranchesAsync_WithClone_SetsHasCloneFlag()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a clone directory
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clonePath = Path.GetFullPath(Path.Combine(clonesDir, "feature+test"));
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // Mock main repo rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone rev-parse
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature/test" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main|abc123|||2024-01-15|Commit 1'\n'feature/test|def456|||2024-01-16|Commit 2'"
            });

        // Act
        var result = await _service.ListLocalBranchesAsync(repoPath);

        // Assert
        var featureBranch = result.FirstOrDefault(b => b.ShortName == "feature/test");
        Assert.That(featureBranch, Is.Not.Null);
        Assert.That(featureBranch!.HasClone, Is.True);
        Assert.That(featureBranch.ClonePath, Is.EqualTo(clonePath));
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

    #region GetCloneStatusAsync Tests

    [Test]
    public async Task GetCloneStatusAsync_CleanClone_ReturnsZeroCounts()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetCloneStatusAsync(clonePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(0));
        Assert.That(result.StagedCount, Is.EqualTo(0));
        Assert.That(result.UntrackedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetCloneStatusAsync_ModifiedFiles_ReturnsCorrectCount()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", clonePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = " M src/file1.cs\n M src/file2.cs\n M src/file3.cs"
            });

        // Act
        var result = await _service.GetCloneStatusAsync(clonePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(3));
    }

    [Test]
    public async Task GetCloneStatusAsync_MixedStatus_ReturnsCorrectCounts()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", clonePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "M  src/staged.cs\n M src/modified.cs\nMM src/both.cs\n?? src/untracked.cs\nA  src/added.cs"
            });

        // Act
        var result = await _service.GetCloneStatusAsync(clonePath);

        // Assert
        Assert.That(result.StagedCount, Is.EqualTo(3)); // M, MM, A in first position
        Assert.That(result.ModifiedCount, Is.EqualTo(2)); // M, MM in second position
        Assert.That(result.UntrackedCount, Is.EqualTo(1)); // ??
    }

    [Test]
    public async Task GetCloneStatusAsync_GitError_ReturnsEmptyStatus()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", clonePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.GetCloneStatusAsync(clonePath);

        // Assert
        Assert.That(result.ModifiedCount, Is.EqualTo(0));
        Assert.That(result.StagedCount, Is.EqualTo(0));
        Assert.That(result.UntrackedCount, Is.EqualTo(0));
    }

    #endregion

    #region FindLostCloneFoldersAsync Tests

    [Test]
    public async Task FindLostCloneFoldersAsync_NoLostFolders_ReturnsEmptyList()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.FindLostCloneFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task FindLostCloneFoldersAsync_WithLostFolders_ReturnsLostPaths()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a sibling folder that's not tracked (has .git file)
        var lostFolder = Path.Combine(_tempDir, "feature-abandoned");
        Directory.CreateDirectory(lostFolder);
        File.WriteAllText(Path.Combine(lostFolder, ".git"), "gitdir: /some/path");

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.FindLostCloneFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo(Path.GetFullPath(lostFolder)));
    }

    [Test]
    public async Task FindLostCloneFoldersAsync_IgnoresNonGitFolders_ReturnsOnlyGitFolders()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a sibling folder that looks like a git repo (has .git)
        var lostFolder = Path.Combine(_tempDir, "feature-lost");
        Directory.CreateDirectory(lostFolder);
        File.WriteAllText(Path.Combine(lostFolder, ".git"), "gitdir: /some/path");

        // Create a non-git folder
        var regularFolder = Path.Combine(_tempDir, "regular-folder");
        Directory.CreateDirectory(regularFolder);

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.FindLostCloneFoldersAsync(repoPath);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo(Path.GetFullPath(lostFolder)));
    }

    [Test]
    public async Task FindLostCloneFoldersAsync_ScansLegacyWorktreesDir()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create legacy .worktrees directory with a folder (backward compatibility)
        var legacyDir = Path.Combine(_tempDir, ".worktrees", "legacy-clone");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".git"), "gitdir: /some/path");

        // Mock main repo
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.FindLostCloneFoldersAsync(repoPath);

        // Assert - Should find the folder in legacy .worktrees directory
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo(Path.GetFullPath(legacyDir)));
    }

    #endregion

    #region DeleteCloneFolderAsync Tests

    [Test]
    public async Task DeleteCloneFolderAsync_FolderExists_DeletesFolder()
    {
        // Arrange
        var folderPath = Path.Combine(_tempDir, "folder-to-delete");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        // Act
        var result = await _service.DeleteCloneFolderAsync(folderPath);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(Directory.Exists(folderPath), Is.False);
    }

    [Test]
    public async Task DeleteCloneFolderAsync_FolderDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var folderPath = Path.Combine(_tempDir, "nonexistent-folder");

        // Act
        var result = await _service.DeleteCloneFolderAsync(folderPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region GetCurrentBranchAsync Tests

    [Test]
    public async Task GetCurrentBranchAsync_Success_ReturnsBranchName()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature/test-branch" });

        // Act
        var result = await _service.GetCurrentBranchAsync(clonePath);

        // Assert
        Assert.That(result, Is.EqualTo("feature/test-branch"));
    }

    [Test]
    public async Task GetCurrentBranchAsync_DetachedHead_ReturnsHEAD()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "HEAD" });

        // Act
        var result = await _service.GetCurrentBranchAsync(clonePath);

        // Assert
        Assert.That(result, Is.EqualTo("HEAD"));
    }

    [Test]
    public async Task GetCurrentBranchAsync_GitError_ReturnsNull()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");

        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "not a git repository" });

        // Act
        var result = await _service.GetCurrentBranchAsync(clonePath);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region CheckoutBranchAsync Tests

    [Test]
    public async Task CheckoutBranchAsync_Success_ReturnsTrue()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        var branchName = "feature/test";

        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", clonePath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CheckoutBranchAsync(clonePath, branchName);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task CheckoutBranchAsync_BranchDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        var branchName = "nonexistent-branch";

        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", clonePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "error: pathspec 'nonexistent-branch' did not match any file(s) known to git" });

        // Act
        var result = await _service.CheckoutBranchAsync(clonePath, branchName);

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

        _mockRunner.Setup(r => r.RunAsync("git", $"log \"{targetBranch}..{branchName}\" --format=%H", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123\ndef456" });

        _mockRunner.Setup(r => r.RunAsync("git", $"cherry \"{targetBranch}\" \"{branchName}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

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

        _mockRunner.Setup(r => r.RunAsync("git", $"cherry \"{targetBranch}\" \"{branchName}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "+ abc123" });

        // Act
        var result = await _service.IsSquashMergedAsync(repoPath, branchName, targetBranch);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region CreateCloneFromRemoteBranchAsync Tests

    [Test]
    public async Task CreateCloneFromRemoteBranchAsync_Success_CreatesCloneWithoutCheckingOutInMain()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var remoteBranch = "feature/remote-only";

        // Mock fetch origin
        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock create local branch from remote
        _mockRunner.Setup(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock remote set-url in clone
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.Is<string>(s => s.Contains(".clones"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock checkout in clone
        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{remoteBranch}\"", It.Is<string>(s => s.Contains(".clones"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateCloneFromRemoteBranchAsync(repoPath, remoteBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Verify git branch was called (not checkout which would change the main repo)
        _mockRunner.Verify(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath), Times.Once);
    }

    [Test]
    public async Task CreateCloneFromRemoteBranchAsync_BranchAlreadyExists_UsesExistingBranch()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);
        var remoteBranch = "feature/existing";

        // Mock fetch origin
        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Branch creation fails because it already exists
        _mockRunner.Setup(r => r.RunAsync("git", $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"", repoPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: a branch named 'feature/existing' already exists" });

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock remote set-url in clone
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.Is<string>(s => s.Contains(".clones"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Mock checkout in clone
        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{remoteBranch}\"", It.Is<string>(s => s.Contains(".clones"))))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.CreateCloneFromRemoteBranchAsync(repoPath, remoteBranch);

        // Assert
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region GetChangedFilesAsync Tests

    [Test]
    public async Task GetChangedFilesAsync_WithChanges_ReturnsFileList()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "10\t5\tsrc/Components/Button.cs\n3\t0\tsrc/Services/MyService.cs\n0\t15\tsrc/Old/Deprecated.cs"
            });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

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
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChangedFilesAsync_ParsesAdditions_Correctly()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "100\t0\tnew-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

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
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t50\tdeleted-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

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
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "25\t0\tnew-feature.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Added));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsDeletedFiles()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t30\told-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Deleted));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsModifiedFiles()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "10\t5\tmodified-file.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public async Task GetChangedFilesAsync_DetectsRenamedFiles()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t0\told-name.cs => new-name.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Renamed));
        Assert.That(result[0].FilePath, Does.Contain("=>"));
    }

    [Test]
    public async Task GetChangedFilesAsync_GitError_ReturnsEmptyList()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "fatal: not a git repository" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChangedFilesAsync_BinaryFiles_HandlesCorrectly()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "-\t-\timage.png" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FilePath, Is.EqualTo("image.png"));
        Assert.That(result[0].Additions, Is.EqualTo(0));
        Assert.That(result[0].Deletions, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public async Task GetChangedFilesAsync_PathsWithSpaces_HandlesCorrectly()
    {
        // Arrange
        var clonePath = Path.Combine(_tempDir, "clone");
        Directory.CreateDirectory(clonePath);

        _mockRunner.Setup(r => r.RunAsync("git", "diff --numstat main...HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "5\t3\tsrc/My Components/Button Component.cs" });

        // Act
        var result = await _service.GetChangedFilesAsync(clonePath, "main");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].FilePath, Is.EqualTo("src/My Components/Button Component.cs"));
    }

    #endregion

    #region ListClonesAsync - ExpectedBranch Detection Tests

    [Test]
    [Description("Regression test for issue 1JudQJ: Clone folder names use flattened format (feature+test) but branch matching was using slash-preserving format")]
    public async Task ListClonesAsync_WithSlashesInBranchName_DetectsBranchMismatchCorrectly()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a clone directory with flattened name (slashes become plus) and .git marker
        var clonePath = Path.Combine(_tempDir, ".clones", "feature+my-branch+abc123");
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // The actual branch name has slashes
        var actualBranchName = "feature/my-branch+abc123";

        // Mock main repo branch/commit
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone - on a different branch than expected
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "wrong-branch" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock branch list showing the expected branch exists
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"'main|abc123|||2024-01-15|Main'\n'{actualBranchName}|def456|||2024-01-16|Feature'"
            });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        var clone = result.FirstOrDefault(w => w.Path == Path.GetFullPath(clonePath));
        Assert.That(clone, Is.Not.Null, "Clone should be found");
        Assert.That(clone!.ExpectedBranch, Is.EqualTo(actualBranchName),
            "ExpectedBranch should be set to the branch matching the flattened folder name");
        Assert.That(clone.IsOnCorrectBranch, Is.False,
            "IsOnCorrectBranch should be false since clone is on 'wrong-branch' but should be on '{0}'", actualBranchName);
    }

    [Test]
    [Description("Test that clone on correct branch is detected when branch name has slashes")]
    public async Task ListClonesAsync_CloneOnCorrectBranchWithSlashes_NoExpectedBranchSet()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a clone directory with flattened name (slashes become plus) and .git marker
        var clonePath = Path.Combine(_tempDir, ".clones", "feature+my-branch+xyz789");
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // The actual branch name has slashes
        var branchName = "feature/my-branch+xyz789";

        // Mock main repo branch/commit
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone - on the correct branch (matching the slashed version of the folder name)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = branchName });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock branch list showing the branch exists
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"'main|abc123|||2024-01-15|Main'\n'{branchName}|def456|||2024-01-16|Feature'"
            });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        var clone = result.FirstOrDefault(w => w.Path == Path.GetFullPath(clonePath));
        Assert.That(clone, Is.Not.Null, "Clone should be found");
        Assert.That(clone!.ExpectedBranch, Is.Null,
            "ExpectedBranch should be null when clone is on the correct branch");
        Assert.That(clone.IsOnCorrectBranch, Is.True,
            "IsOnCorrectBranch should be true since clone is on the expected branch");
    }

    [Test]
    [Description("Test that clone folder name with multiple slashes is matched correctly")]
    public async Task ListClonesAsync_WithMultipleSlashesInBranchName_MatchesCorrectly()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a clone directory with multiple slashes flattened to plus signs and .git marker
        var clonePath = Path.Combine(_tempDir, ".clones", "feature+area+subfeature+abc");
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // The actual branch name has multiple slashes
        var branchName = "feature/area/subfeature+abc";

        // Mock main repo branch/commit
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone - on wrong branch
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "other" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock branch list showing the expected branch exists
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = $"'main|abc123|||2024-01-15|Main'\n'{branchName}|def456|||2024-01-16|Feature'\n'other|ghi789|||2024-01-17|Other'"
            });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        var clone = result.FirstOrDefault(w => w.Path == Path.GetFullPath(clonePath));
        Assert.That(clone, Is.Not.Null, "Clone should be found");
        Assert.That(clone!.ExpectedBranch, Is.EqualTo(branchName),
            "ExpectedBranch should match the branch with slashes that corresponds to the flattened folder name");
        Assert.That(clone.IsOnCorrectBranch, Is.False,
            "IsOnCorrectBranch should be false since clone is on 'other' not '{0}'", branchName);
    }

    #endregion

    #region GetWorkdirPath Tests

    [Test]
    public void GetWorkdirPath_ReturnsWorkdirSubpath()
    {
        // Arrange
        var clonePath = "/data/repos/myproject/.clones/feature+test";

        // Act
        var result = GitCloneService.GetWorkdirPath(clonePath);

        // Assert
        Assert.That(result, Is.EqualTo("/data/repos/myproject/.clones/feature+test/workdir"));
    }

    [Test]
    public void GetWorkdirPath_HandlesTrailingSlash()
    {
        // Arrange
        var clonePath = "/data/repos/myproject/.clones/feature+test/";

        // Act
        var result = GitCloneService.GetWorkdirPath(clonePath);

        // Assert
        Assert.That(result, Does.EndWith("workdir"));
    }

    #endregion

    #region CreateCloneAsync - New Directory Structure Tests

    [Test]
    public async Task CreateCloneAsync_Success_CreatesClaudeDirectory()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test-claude";

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local - note: now clones into workdir subdirectory
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url in the clone directory (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock checkout in the clone directory (in workdir)
        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", It.Is<string>(s => s.Contains("workdir"))))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Verify .claude directory was created
        var claudeDir = Path.Combine(result!, ".claude");
        Assert.That(Directory.Exists(claudeDir), Is.True, "Expected .claude directory to be created");
    }

    [Test]
    public async Task CreateCloneAsync_Success_ClonesIntoWorkdirSubdirectory()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoPath);
        var branchName = "feature/test-workdir";
        string? clonedPath = null;

        // Mock remote get-url
        _mockRunner.Setup(r => r.RunAsync("git", "remote get-url origin", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "https://github.com/user/repo.git" });

        // Mock clone --local - capture the clone destination path
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("clone --local")), repoPath))
            .Callback<string, string, string>((_, args, _) =>
            {
                // Extract the destination path from: clone --local "source" "dest"
                var parts = args.Split('"');
                if (parts.Length >= 4)
                {
                    clonedPath = parts[3];
                }
            })
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock remote set-url
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.StartsWith("remote set-url")), It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Mock checkout
        _mockRunner.Setup(r => r.RunAsync("git", $"checkout \"{branchName}\"", It.IsAny<string>()))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.CreateCloneAsync(repoPath, branchName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(clonedPath, Is.Not.Null);
        Assert.That(clonedPath, Does.EndWith("workdir"), "Git clone should target workdir subdirectory");
    }

    #endregion

    #region ListClonesRawAsync - New Directory Structure Tests

    [Test]
    public async Task ListClonesAsync_NewStructure_FindsClonesWithWorkdirSubdirectory()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create .clones directory with new structure: clone/workdir/.git
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clone1 = Path.Combine(clonesDir, "feature-1");
        var clone1Workdir = Path.Combine(clone1, "workdir");
        Directory.CreateDirectory(clone1Workdir);
        Directory.CreateDirectory(Path.Combine(clone1Workdir, ".git"));
        Directory.CreateDirectory(Path.Combine(clone1, ".claude"));

        // Mock main repo branch/commit
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone 1 (workdir subdirectory)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clone1Workdir))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature-1" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clone1Workdir))
            .ReturnsAsync(new CommandResult { Success = true, Output = "def456" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.ListClonesAsync(repoPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2)); // main + 1 clone
        var clone = result.FirstOrDefault(c => c.Path.Contains("feature-1"));
        Assert.That(clone, Is.Not.Null);
    }

    #endregion

    #region PruneClonesAsync - New Directory Structure Tests

    [Test]
    public async Task PruneClonesAsync_NewStructure_ChecksWorkdirGit()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create .clones directory with new structure
        var clonesDir = Path.Combine(_tempDir, ".clones");

        // Valid clone with workdir/.git
        var validClone = Path.Combine(clonesDir, "valid-clone");
        var validWorkdir = Path.Combine(validClone, "workdir");
        Directory.CreateDirectory(validWorkdir);
        Directory.CreateDirectory(Path.Combine(validWorkdir, ".git"));

        // Broken clone without workdir/.git
        var brokenClone = Path.Combine(clonesDir, "broken-clone");
        Directory.CreateDirectory(brokenClone);
        // No workdir/.git directory

        // Act
        await _service.PruneClonesAsync(repoPath);

        // Assert
        Assert.That(Directory.Exists(brokenClone), Is.False, "Broken clone without workdir/.git should be deleted");
        Assert.That(Directory.Exists(validClone), Is.True, "Valid clone with workdir/.git should be preserved");
    }

    #endregion

    #region ListLocalBranchesAsync - Duplicate Branch Regression Tests

    [Test]
    [Description("Regression test for FJTVh5: Main repo and clone both on 'main' branch should not throw duplicate key exception")]
    public async Task ListLocalBranchesAsync_MainRepoAndCloneOnSameBranch_DoesNotThrow()
    {
        // Arrange
        var repoPath = Path.Combine(_tempDir, "main");
        Directory.CreateDirectory(repoPath);

        // Create a clone directory that is also on "main" (simulates a fresh clone before checkout)
        var clonesDir = Path.Combine(_tempDir, ".clones");
        var clonePath = Path.GetFullPath(Path.Combine(clonesDir, "feature+test"));
        Directory.CreateDirectory(clonePath);
        Directory.CreateDirectory(Path.Combine(clonePath, ".git"));

        // Mock main repo on "main" branch
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", repoPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock clone also on "main" branch (duplicate key scenario)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main" });
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse HEAD", clonePath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "abc123" });

        // Mock for-each-ref
        _mockRunner.Setup(r => r.RunAsync("git", It.Is<string>(s => s.Contains("for-each-ref")), repoPath))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "'main|abc123|||2024-01-15|Initial commit'\n'feature/test|def456|||2024-01-16|Add feature'"
            });

        // Act & Assert - should not throw ArgumentException about duplicate key
        Assert.DoesNotThrowAsync(async () => await _service.ListLocalBranchesAsync(repoPath));

        var result = await _service.ListLocalBranchesAsync(repoPath);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
    }

    #endregion
}
