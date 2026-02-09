using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class FleeceIssuesSyncServiceTests
{
    private Mock<ICommandRunner> _mockRunner = null!;
    private FleeceIssuesSyncService _service = null!;
    private string _projectPath = null!;
    private const string DefaultBranch = "main";

    // Convenience property matching old naming pattern used in all tests
    private string ProjectPath => _projectPath;

    [SetUp]
    public void SetUp()
    {
        _mockRunner = new Mock<ICommandRunner>();
        _service = new FleeceIssuesSyncService(_mockRunner.Object, Mock.Of<ILogger<FleeceIssuesSyncService>>());

        // Create a real temp dir for tests that trigger MergeFleeceFromRemoteAsync
        _projectPath = Path.Combine(Path.GetTempPath(), $"fleece-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_projectPath, ".fleece"));
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_projectPath))
                Directory.Delete(_projectPath, recursive: true);
        }
        catch { /* best effort */ }
    }

    #region CheckBranchStatusAsync Tests

    [Test]
    public async Task CheckBranchStatusAsync_OnCorrectBranch_ReturnsSuccess()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t0" });

        // Act
        var result = await _service.CheckBranchStatusAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.IsOnCorrectBranch, Is.True);
        Assert.That(result.CurrentBranch, Is.EqualTo("main"));
        Assert.That(result.IsBehindRemote, Is.False);
        Assert.That(result.CommitsBehind, Is.EqualTo(0));
        Assert.That(result.CommitsAhead, Is.EqualTo(0));
    }

    [Test]
    public async Task CheckBranchStatusAsync_OnWrongBranch_ReturnsError()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature/other-branch\n" });

        // Act
        var result = await _service.CheckBranchStatusAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.IsOnCorrectBranch, Is.False);
        Assert.That(result.CurrentBranch, Is.EqualTo("feature/other-branch"));
        Assert.That(result.ErrorMessage, Does.Contain("feature/other-branch"));
        Assert.That(result.ErrorMessage, Does.Contain("main"));
    }

    [Test]
    public async Task CheckBranchStatusAsync_BehindRemote_ReturnsCorrectStatus()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "2\t1" });

        // Act
        var result = await _service.CheckBranchStatusAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.IsBehindRemote, Is.True);
        Assert.That(result.CommitsBehind, Is.EqualTo(2));
        Assert.That(result.CommitsAhead, Is.EqualTo(1));
    }

    [Test]
    public async Task CheckBranchStatusAsync_FetchFails_ReturnsError()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "network error" });

        // Act
        var result = await _service.CheckBranchStatusAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Failed to fetch"));
    }

    #endregion

    #region SyncAsync Tests

    [Test]
    public async Task SyncAsync_NoChangesAndUpToDate_ReturnsSuccess()
    {
        // Arrange - On correct branch, no changes, up to date
        SetupBranchCheck(isOnBranch: true, commitsBehind: 0, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.FilesCommitted, Is.EqualTo(0));
        Assert.That(result.PushSucceeded, Is.True);
    }

    [Test]
    public async Task SyncAsync_WithFleeceChanges_CommitsAndPushes()
    {
        // Arrange
        SetupBranchCheck(isOnBranch: true, commitsBehind: 0, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl\n M .fleece/changes.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl\n M .fleece/changes.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.FilesCommitted, Is.EqualTo(2));
        Assert.That(result.PushSucceeded, Is.True);
    }

    [Test]
    public async Task SyncAsync_OnWrongBranch_ReturnsError()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "feature/wrong-branch\n" });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("feature/wrong-branch"));
        Assert.That(result.ErrorMessage, Does.Contain("main"));
    }

    [Test]
    public async Task SyncAsync_BehindRemoteWithNoNonFleeceChanges_MergesAndPushes()
    {
        // Arrange - Behind remote but no non-fleece changes
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // First call returns 2 behind, second call (after merge) returns 0 behind 1 ahead
        _mockRunner.SetupSequence(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "2\t0" })  // First call: 2 behind, 0 ahead
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" }); // Second call: 0 behind, 1 ahead

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // The new merge approach: restore from remote, then git merge
        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.PushSucceeded, Is.True);

        // Verify the new merge approach was used instead of pull --rebase
        _mockRunner.Verify(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath), Times.Once);

        // Verify old approach was NOT used
        _mockRunner.Verify(r => r.RunAsync("git", "pull origin main --rebase", ProjectPath), Times.Never);
    }

    [Test]
    public async Task SyncAsync_BehindRemoteWithNonFleeceChanges_MergesSuccessfully()
    {
        // Arrange - Behind remote with non-fleece changes (the new approach handles this via git merge)
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.SetupSequence(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "2\t0" })
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl\n M src/SomeFile.cs\n M README.md" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert - the new approach handles non-fleece changes via git merge, no error
        Assert.That(result.Success, Is.True);
        Assert.That(result.PushSucceeded, Is.True);
    }

    [Test]
    public async Task SyncAsync_PushRejected_ReturnsErrorWithRetryHint()
    {
        // Arrange
        SetupBranchCheck(isOnBranch: true, commitsBehind: 0, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "! [rejected] main -> main (non-fast-forward)" });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresPullFirst, Is.True);
        Assert.That(result.ErrorMessage, Does.Contain("remote has new changes"));
    }

    [Test]
    public async Task SyncAsync_MergeFails_AbortsMergeAndReturnsError()
    {
        // Arrange - Behind remote, git merge fails with non-fleece conflicts
        SetupBranchCheck(isOnBranch: true, commitsBehind: 2, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "CONFLICT (content): Merge conflict in file.txt" });

        // TryResolveFleeceConflictsAsync: non-fleece conflicts exist
        _mockRunner.Setup(r => r.RunAsync("git", "diff --name-only --diff-filter=U", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "file.txt\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "merge --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresPullFirst, Is.True);
        Assert.That(result.ErrorMessage, Does.Contain("aborted"));

        // Verify merge was aborted (not rebase)
        _mockRunner.Verify(r => r.RunAsync("git", "merge --abort", ProjectPath), Times.Once);
    }

    [Test]
    public async Task SyncAsync_MergeConflictOnlyInFleece_ResolvesAutomatically()
    {
        // Arrange - Behind remote, merge fails but only .fleece/ files conflict
        SetupBranchCheck(isOnBranch: true, commitsBehind: 2, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "CONFLICT (content): Merge conflict in .fleece/issues.jsonl" });

        // TryResolveFleeceConflictsAsync: only .fleece/ files in conflict
        _mockRunner.Setup(r => r.RunAsync("git", "diff --name-only --diff-filter=U", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = ".fleece/issues.jsonl\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "checkout --ours -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.SetupSequence(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "2\t0" })
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert - .fleece/ conflicts were auto-resolved, sync succeeds
        Assert.That(result.Success, Is.True);
        Assert.That(result.PushSucceeded, Is.True);

        // Verify conflict resolution was applied
        _mockRunner.Verify(r => r.RunAsync("git", "checkout --ours -- .fleece/", ProjectPath), Times.Once);
    }

    #endregion

    #region DiscardNonFleeceChangesAsync Tests

    [Test]
    public async Task DiscardNonFleeceChangesAsync_NoNonFleeceChanges_ReturnsTrue()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rebase --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false }); // No rebase in progress

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "clean -fd --exclude=.fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DiscardNonFleeceChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DiscardNonFleeceChangesAsync_WithNonFleeceChanges_DiscardsOnlyThoseFiles()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rebase --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues.jsonl\n M src/File.cs\n?? newfile.txt" });

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -- \"src/File.cs\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -- \"newfile.txt\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false }); // Untracked file

        _mockRunner.Setup(r => r.RunAsync("git", "clean -f -- \"newfile.txt\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "clean -fd --exclude=.fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DiscardNonFleeceChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.True);

        // Verify checkout was called for tracked file
        _mockRunner.Verify(r => r.RunAsync("git", "checkout -- \"src/File.cs\"", ProjectPath), Times.Once);

        // Verify clean was called for untracked file
        _mockRunner.Verify(r => r.RunAsync("git", "clean -f -- \"newfile.txt\"", ProjectPath), Times.Once);

        // Verify fleece file was NOT touched
        _mockRunner.Verify(r => r.RunAsync("git", It.Is<string>(s => s.Contains(".fleece/issues.jsonl")), ProjectPath), Times.Never);
    }

    #endregion

    #region PullChangesAsync Tests

    [Test]
    public async Task PullChangesAsync_Success_ReturnsSuccess()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // The new approach: restore from remote, then merge
        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.PullChangesAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.HasConflicts, Is.False);

        // Verify the new approach was used
        _mockRunner.Verify(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath), Times.Once);

        // Verify old approach was NOT used
        _mockRunner.Verify(r => r.RunAsync("git", "pull origin main --rebase", ProjectPath), Times.Never);
    }

    [Test]
    public async Task PullChangesAsync_WithConflict_AbortsMergeAndReturnsConflictInfo()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M src/File.cs" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "CONFLICT: Merge conflict in src/File.cs" });

        // TryResolveFleeceConflictsAsync: non-fleece conflicts
        _mockRunner.Setup(r => r.RunAsync("git", "diff --name-only --diff-filter=U", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "src/File.cs\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "merge --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.PullChangesAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.HasConflicts, Is.True);
        Assert.That(result.HasNonFleeceChanges, Is.True);
        Assert.That(result.NonFleeceChangedFiles, Does.Contain("src/File.cs"));

        // Verify merge --abort was called (not rebase --abort)
        _mockRunner.Verify(r => r.RunAsync("git", "merge --abort", ProjectPath), Times.Once);
    }

    #endregion

    #region DiscardChangesAsync Tests

    [Test]
    public async Task DiscardChangesAsync_Success_ReturnsTrue()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rebase --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "reset HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -- .", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "clean -fd", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.DiscardChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DiscardChangesAsync_CheckoutFails_ReturnsFalse()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "rebase --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false });

        _mockRunner.Setup(r => r.RunAsync("git", "reset HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "checkout -- .", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "error" });

        // Act
        var result = await _service.DiscardChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region StashChangesAsync Tests

    [Test]
    public async Task StashChangesAsync_Success_ReturnsTrue()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "stash push -m \"fleece-sync-auto-stash\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.StashChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task StashChangesAsync_Fails_ReturnsFalse()
    {
        // Arrange
        _mockRunner.Setup(r => r.RunAsync("git", "stash push -m \"fleece-sync-auto-stash\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "cannot stash" });

        // Act
        var result = await _service.StashChangesAsync(ProjectPath);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region UpdateMainBranch Scenario Tests

    [Test]
    [Description("Scenario: Main branch is behind remote with no fleece changes. Sync should merge and succeed.")]
    public async Task SyncAsync_MainBranchBehindRemoteNoFleeceChanges_MergesAndReturnsSuccess()
    {
        // Arrange - Behind remote, no fleece changes, no non-fleece changes
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.SetupSequence(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "3\t0" })   // First call: 3 behind, 0 ahead
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t0" });  // After merge: up to date

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // New merge approach
        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FilesCommitted, Is.EqualTo(0));
            Assert.That(result.PushSucceeded, Is.True);
        });

        // Verify merge was used to bring main up to date
        _mockRunner.Verify(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath), Times.Once);
    }

    [Test]
    [Description("Scenario: Main branch is behind remote with pending fleece changes. Sync should commit fleece, merge, and push.")]
    public async Task SyncAsync_MainBranchBehindRemoteWithFleeceChanges_CommitsMergesAndPushes()
    {
        // Arrange - Behind remote with pending fleece changes
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "main\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.SetupSequence(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "5\t0" })   // First: 5 behind
            .ReturnsAsync(new CommandResult { Success = true, Output = "0\t1" });  // After merge: 1 ahead (the fleece commit)

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues_abc.jsonl\n M .fleece/changes_abc.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = " M .fleece/issues_abc.jsonl\n M .fleece/changes_abc.jsonl" });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // New merge approach
        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "push origin main", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FilesCommitted, Is.EqualTo(2));
            Assert.That(result.PushSucceeded, Is.True);
        });

        // Verify the full workflow: commit fleece, merge from remote, push
        _mockRunner.Verify(r => r.RunAsync("git", "add .fleece/", ProjectPath), Times.AtLeastOnce);
        _mockRunner.Verify(r => r.RunAsync("git", "commit -m \"chore: sync fleece issues\"", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath), Times.Once);
        _mockRunner.Verify(r => r.RunAsync("git", "push origin main", ProjectPath), Times.Once);
    }

    [Test]
    [Description("Scenario: Main branch is behind remote but merge encounters a non-fleece conflict. Sync should abort merge and report error.")]
    public async Task SyncAsync_MainBranchBehindRemoteMergeConflict_AbortsAndReturnsError()
    {
        // Arrange
        SetupBranchCheck(isOnBranch: true, commitsBehind: 3, commitsAhead: 0);

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        _mockRunner.Setup(r => r.RunAsync("git", "status --porcelain", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "" });

        // New merge approach
        _mockRunner.Setup(r => r.RunAsync("git", "restore --source origin/main -- .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "add .fleece/", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "merge origin/main --no-edit", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = false, Error = "CONFLICT (content): Merge conflict in src/Program.cs" });

        // TryResolveFleeceConflictsAsync: non-fleece conflicts
        _mockRunner.Setup(r => r.RunAsync("git", "diff --name-only --diff-filter=U", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = "src/Program.cs\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "merge --abort", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        // Act
        var result = await _service.SyncAsync(ProjectPath, DefaultBranch);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.RequiresPullFirst, Is.True);
            Assert.That(result.ErrorMessage, Does.Contain("aborted"));
        });

        // Verify merge was properly cleaned up
        _mockRunner.Verify(r => r.RunAsync("git", "merge --abort", ProjectPath), Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupBranchCheck(bool isOnBranch, int commitsBehind, int commitsAhead)
    {
        _mockRunner.Setup(r => r.RunAsync("git", "rev-parse --abbrev-ref HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = isOnBranch ? "main\n" : "other-branch\n" });

        _mockRunner.Setup(r => r.RunAsync("git", "fetch origin", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true });

        _mockRunner.Setup(r => r.RunAsync("git", "rev-list --left-right --count origin/main...HEAD", ProjectPath))
            .ReturnsAsync(new CommandResult { Success = true, Output = $"{commitsBehind}\t{commitsAhead}" });
    }

    #endregion
}
