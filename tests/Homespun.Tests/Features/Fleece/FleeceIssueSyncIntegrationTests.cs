using System.Diagnostics;
using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Services;
using Homespun.Features.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
[Category("Integration")]
public class FleeceIssueSyncIntegrationTests
{
    private string _tempDir = null!;
    private string _originPath = null!;
    private string _cloneAPath = null!;
    private string _cloneBPath = null!;
    private readonly IssueMerger _merger = new();

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fleece-sync-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _originPath = Path.Combine(_tempDir, "origin.git");
        _cloneAPath = Path.Combine(_tempDir, "clone-a");
        _cloneBPath = Path.Combine(_tempDir, "clone-b");

        // Create a bare origin repo with 'main' as default branch
        RunGit($"init --bare --initial-branch=main \"{_originPath}\"");

        // Clone to clone-a
        RunGitIn(_tempDir, $"clone \"{_originPath}\" clone-a");
        ConfigureGit(_cloneAPath, "user-a@test.com", "User A");

        // Ensure we're on main branch and create initial commit
        RunGitIn(_cloneAPath, "checkout -b main");
        Directory.CreateDirectory(Path.Combine(_cloneAPath, ".fleece"));
        File.WriteAllText(Path.Combine(_cloneAPath, "README.md"), "# Test Repo");
        RunGitIn(_cloneAPath, "add .");
        RunGitIn(_cloneAPath, "commit -m \"Initial commit\"");
        RunGitIn(_cloneAPath, "push -u origin main");

        // Clone to clone-b
        RunGitIn(_tempDir, $"clone \"{_originPath}\" clone-b");
        ConfigureGit(_cloneBPath, "user-b@test.com", "User B");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                ForceDeleteDirectory(_tempDir);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Test]
    public async Task Merge_BothSidesModifyDifferentIssues_BothChangesPreserved()
    {
        // Arrange: Create two issues in both clones
        var issueA = CreateIssue("AAAAAA", "Issue A", IssueStatus.Open);
        var issueB = CreateIssue("BBBBBB", "Issue B", IssueStatus.Open);
        var baseIssues = new List<Issue> { issueA, issueB };

        await SaveAndCommitIssues(_cloneAPath, baseIssues, "Add initial issues");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A modifies Issue A's title
        var modifiedA = issueA with
        {
            Title = "Issue A - Modified by Clone A",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [modifiedA, issueB], "Modify issue A");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B modifies Issue B's status (diverges from origin)
        var modifiedB = issueB with
        {
            Status = IssueStatus.Progress,
            StatusLastUpdate = DateTimeOffset.UtcNow,
            StatusModifiedBy = "user-b"
        };
        await SaveAndCommitIssues(_cloneBPath, [issueA, modifiedB], "Modify issue B");

        // Act: Simulate the merge strategy - load local, restore from remote, load remote, merge
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert
        Assert.That(merged, Has.Count.EqualTo(2));

        var mergedA = merged.First(i => i.Id == "AAAAAA");
        var mergedB = merged.First(i => i.Id == "BBBBBB");

        // Issue A should have the title from clone A (remote)
        Assert.That(mergedA.Title, Is.EqualTo("Issue A - Modified by Clone A"));
        // Issue B should have the status from clone B (local)
        Assert.That(mergedB.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task Merge_SameIssueDifferentFields_BothFieldChangesPreserved()
    {
        // Arrange: Create an issue in both clones
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issue = CreateIssue("CCCCCC", "Original Title", IssueStatus.Open, baseTime);
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add initial issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A changes the title
        var modifiedByA = issue with
        {
            Title = "Title Changed by A",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [modifiedByA], "Change title");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B changes the status
        var modifiedByB = issue with
        {
            Status = IssueStatus.Progress,
            StatusLastUpdate = DateTimeOffset.UtcNow,
            StatusModifiedBy = "user-b"
        };
        await SaveAndCommitIssues(_cloneBPath, [modifiedByB], "Change status");

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert
        Assert.That(merged, Has.Count.EqualTo(1));
        var mergedIssue = merged[0];

        // Both changes should be preserved (different fields)
        Assert.That(mergedIssue.Title, Is.EqualTo("Title Changed by A"));
        Assert.That(mergedIssue.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task Merge_SameIssueSameFieldDifferentTimes_NewerTimestampWins()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var issue = CreateIssue("DDDDDD", "Original Title", IssueStatus.Open, baseTime);
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add initial issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A changes title earlier
        var earlierTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var modifiedByA = issue with
        {
            Title = "Title by A (earlier)",
            TitleLastUpdate = earlierTime,
            TitleModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [modifiedByA], "Change title (earlier)");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B changes same title later
        var laterTime = DateTimeOffset.UtcNow;
        var modifiedByB = issue with
        {
            Title = "Title by B (later)",
            TitleLastUpdate = laterTime,
            TitleModifiedBy = "user-b"
        };
        await SaveAndCommitIssues(_cloneBPath, [modifiedByB], "Change title (later)");

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert - the later timestamp should win
        Assert.That(merged, Has.Count.EqualTo(1));
        Assert.That(merged[0].Title, Is.EqualTo("Title by B (later)"));
    }

    [Test]
    public async Task Merge_NewIssuesOnBothSides_BothExistAfterMerge()
    {
        // Arrange: Start with empty .fleece/
        await SaveAndCommitIssues(_cloneAPath, [], "Initialize empty fleece");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A adds Issue X
        var issueX = CreateIssue("XXXXXX", "Issue X from remote");
        await SaveAndCommitIssues(_cloneAPath, [issueX], "Add issue X");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B adds Issue Y
        var issueY = CreateIssue("YYYYYY", "Issue Y from local");
        await SaveAndCommitIssues(_cloneBPath, [issueY], "Add issue Y");

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert - both issues should exist
        Assert.That(merged, Has.Count.EqualTo(2));
        Assert.That(merged.Any(i => i.Id == "XXXXXX"), Is.True, "Issue X from remote should be present");
        Assert.That(merged.Any(i => i.Id == "YYYYYY"), Is.True, "Issue Y from local should be present");
    }

    [Test]
    public async Task Merge_DeletedIssueOnRemote_DeletionPreserved()
    {
        // Arrange: Create two issues
        var issueA = CreateIssue("AAAAAA", "Issue A");
        var issueB = CreateIssue("BBBBBB", "Issue B");
        await SaveAndCommitIssues(_cloneAPath, [issueA, issueB], "Add initial issues");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A deletes issue A (marks as Deleted with newer timestamp)
        var deletedA = issueA with
        {
            Status = IssueStatus.Deleted,
            StatusLastUpdate = DateTimeOffset.UtcNow,
            StatusModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [deletedA, issueB], "Delete issue A");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B has not changed anything

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert - deletion should be preserved (remote timestamp is newer)
        var mergedA = merged.First(i => i.Id == "AAAAAA");
        Assert.That(mergedA.Status, Is.EqualTo(IssueStatus.Deleted));
    }

    [Test]
    public async Task Merge_NoRemoteChanges_OnlyLocalChangesPreserved()
    {
        // Arrange: Create an issue in both clones
        var issue = CreateIssue("EEEEEE", "Issue E");
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add initial issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Only clone B modifies the issue (no changes pushed to origin)
        var modifiedByB = issue with
        {
            Title = "Issue E - Modified Locally",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "user-b"
        };
        await SaveAndCommitIssues(_cloneBPath, [modifiedByB], "Modify issue E");

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert - local changes should be preserved
        Assert.That(merged, Has.Count.EqualTo(1));
        Assert.That(merged[0].Title, Is.EqualTo("Issue E - Modified Locally"));
    }

    [Test]
    public async Task Merge_NoLocalChanges_OnlyRemoteChangesPreserved()
    {
        // Arrange
        var issue = CreateIssue("FFFFFF", "Issue F");
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add initial issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Only clone A modifies the issue and pushes
        var modifiedByA = issue with
        {
            Title = "Issue F - Modified on Remote",
            TitleLastUpdate = DateTimeOffset.UtcNow,
            TitleModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [modifiedByA], "Modify issue F");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B has no changes

        // Act
        var merged = await SimulateMergeFromRemote(_cloneBPath);

        // Assert - remote changes should be preserved
        Assert.That(merged, Has.Count.EqualTo(1));
        Assert.That(merged[0].Title, Is.EqualTo("Issue F - Modified on Remote"));
    }

    #region Helper Methods

    private static Issue CreateIssue(string id, string title, IssueStatus status = IssueStatus.Open, DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        return new Issue
        {
            Id = id,
            Title = title,
            TitleLastUpdate = ts,
            TitleModifiedBy = "test",
            Status = status,
            StatusLastUpdate = ts,
            StatusModifiedBy = "test",
            Type = IssueType.Task,
            TypeLastUpdate = ts,
            TypeModifiedBy = "test",
            CreatedAt = ts,
            LastUpdate = ts,
            CreatedBy = "test"
        };
    }

    private async Task SaveAndCommitIssues(string repoPath, List<Issue> issues, string commitMessage)
    {
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storage = new JsonlStorageService(repoPath, serializer, schemaValidator);

        await storage.EnsureDirectoryExistsAsync(CancellationToken.None);
        await storage.SaveIssuesAsync(issues, CancellationToken.None);

        RunGitIn(repoPath, "add .fleece/");
        RunGitIn(repoPath, $"commit -m \"{commitMessage}\" --allow-empty");
    }

    /// <summary>
    /// Simulates the merge strategy from MergeFleeceFromRemoteAsync:
    /// 1. Load local issues
    /// 2. git restore --source origin/main -- .fleece/ (get remote files)
    /// 3. Load remote issues
    /// 4. Merge per-issue using IssueMerger
    /// </summary>
    private async Task<List<Issue>> SimulateMergeFromRemote(string localRepoPath)
    {
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();

        // 1. Fetch from origin
        RunGitIn(localRepoPath, "fetch origin");

        // 2. Load local issues
        var localStorage = new JsonlStorageService(localRepoPath, serializer, schemaValidator);
        var localIssues = await localStorage.LoadIssuesAsync(CancellationToken.None);
        var localMap = localIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        // 3. Restore remote .fleece/ files
        RunGitIn(localRepoPath, "restore --source origin/main -- .fleece/");

        // 4. Load remote issues
        var remoteStorage = new JsonlStorageService(localRepoPath, serializer, schemaValidator);
        var remoteIssues = await remoteStorage.LoadIssuesAsync(CancellationToken.None);
        var remoteMap = remoteIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        // 5. Merge
        var merged = new List<Issue>();
        var allIds = new HashSet<string>(localMap.Keys, StringComparer.OrdinalIgnoreCase);
        allIds.UnionWith(remoteMap.Keys);

        foreach (var id in allIds)
        {
            var hasLocal = localMap.TryGetValue(id, out var local);
            var hasRemote = remoteMap.TryGetValue(id, out var remote);

            if (hasLocal && hasRemote)
            {
                var result = _merger.Merge(local!, remote!);
                merged.Add(result.MergedIssue);
            }
            else if (hasLocal)
            {
                merged.Add(local!);
            }
            else
            {
                merged.Add(remote!);
            }
        }

        // 6. Write merged result back
        var mergedStorage = new JsonlStorageService(localRepoPath, serializer, schemaValidator);
        await mergedStorage.SaveIssuesAsync(merged, CancellationToken.None);

        return merged;
    }

    private static void ConfigureGit(string repoPath, string email, string name)
    {
        RunGitIn(repoPath, $"config user.email \"{email}\"");
        RunGitIn(repoPath, $"config user.name \"{name}\"");
    }

    private static void RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: git {arguments}\nOutput: {output}\nError: {error}");
        }
    }

    private static void RunGitIn(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git command failed: git {arguments} (in {workingDirectory})\nOutput: {output}\nError: {error}");
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    #endregion

    #region Stash-Pull-Pop-Merge Strategy Tests

    /// <summary>
    /// Tests the stash-pull-pop-merge strategy using the actual FleeceIssuesSyncService.
    /// This validates that local uncommitted .fleece/ changes are stashed before pulling,
    /// then popped and merged after pulling, avoiding divergent commit history.
    /// </summary>
    [Test]
    public async Task SyncAsync_StashPullPopMerge_LocalAndRemoteChanges_NoMergeConflicts()
    {
        // Arrange: Clone A creates an issue and pushes
        var issueA = CreateIssue("AAAAAA", "Issue A from Clone A");
        await SaveAndCommitIssues(_cloneAPath, [issueA], "Add issue A");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B pulls to be up to date
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A creates another issue and pushes (Clone B is now behind)
        var issueA2 = CreateIssue("AAAAAB", "Issue A2 from Clone A");
        await SaveAndCommitIssues(_cloneAPath, [issueA, issueA2], "Add issue A2");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B creates a local issue (uncommitted - just saved to disk)
        var issueB = CreateIssue("BBBBBB", "Issue B from Clone B");
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storageB = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        await storageB.SaveIssuesAsync([issueA, issueB], CancellationToken.None);
        // Don't commit - these are uncommitted local changes

        // Act: Run sync on Clone B using real service
        var syncService = CreateSyncService();
        var result = await syncService.SyncAsync(_cloneBPath, "main");

        // Assert: Sync should succeed without conflicts
        Assert.That(result.Success, Is.True, $"Sync failed: {result.ErrorMessage}");
        Assert.That(result.PushSucceeded, Is.True, "Push should succeed");

        // Verify all issues exist after sync
        var finalStorage = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        var finalIssues = await finalStorage.LoadIssuesAsync(CancellationToken.None);

        Assert.That(finalIssues.Any(i => i.Id == "AAAAAA"), Is.True, "Issue A should exist");
        Assert.That(finalIssues.Any(i => i.Id == "AAAAAB"), Is.True, "Issue A2 should exist");
        Assert.That(finalIssues.Any(i => i.Id == "BBBBBB"), Is.True, "Issue B should exist");
    }

    [Test]
    public async Task SyncAsync_StashPullPopMerge_SameIssueModifiedDifferentFields_BothFieldsPreserved()
    {
        // Arrange: Create initial issue in both clones
        // Use explicit timestamps to avoid race conditions with DateTimeOffset.UtcNow
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var titleModifiedTime = DateTimeOffset.UtcNow.AddSeconds(-5);  // Remote title change
        var statusModifiedTime = DateTimeOffset.UtcNow;  // Local status change (newer than title change)

        var issue = CreateIssue("CCCCCC", "Original Title", IssueStatus.Open, baseTime);
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add initial issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A modifies title and pushes - use explicit timestamp
        var modifiedByA = issue with
        {
            Title = "Title Changed by A",
            TitleLastUpdate = titleModifiedTime,
            TitleModifiedBy = "user-a"
        };
        await SaveAndCommitIssues(_cloneAPath, [modifiedByA], "Change title");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B modifies status (uncommitted - just saved to disk) - use explicit timestamp
        var modifiedByB = issue with
        {
            Status = IssueStatus.Progress,
            StatusLastUpdate = statusModifiedTime,
            StatusModifiedBy = "user-b"
        };
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storageB = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        await storageB.SaveIssuesAsync([modifiedByB], CancellationToken.None);

        // Debug: Check state before sync - need to fetch first to get accurate rev-list
        RunGitIn(_cloneBPath, "fetch origin");
        var (exitCodeBefore, statusBefore, _) = RunGitInWithOutput(_cloneBPath, "status --porcelain .fleece/");
        var (_, revListBefore, _) = RunGitInWithOutput(_cloneBPath, "rev-list --left-right --count origin/main...HEAD");

        // Verify we're actually behind remote
        var parts = revListBefore.Trim().Split('\t');
        Assert.That(parts.Length, Is.GreaterThanOrEqualTo(2), $"Invalid rev-list output: {revListBefore}");
        var behind = int.Parse(parts[0]);
        Assert.That(behind, Is.GreaterThan(0), $"Clone B should be behind remote. rev-list: {revListBefore}, status: {statusBefore}");

        // Act: Run sync on Clone B
        var syncService = CreateSyncService();
        var result = await syncService.SyncAsync(_cloneBPath, "main");

        // Assert: Sync should succeed
        Assert.That(result.Success, Is.True, $"Sync failed: {result.ErrorMessage}. BeforeSync: status='{statusBefore.Trim()}', rev-list='{revListBefore.Trim()}'");

        // Verify both field changes are preserved
        var finalStorage = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        var finalIssues = await finalStorage.LoadIssuesAsync(CancellationToken.None);
        var mergedIssue = finalIssues.First(i => i.Id == "CCCCCC");

        Assert.That(mergedIssue.Title, Is.EqualTo("Title Changed by A"),
            $"Title from remote should be preserved. Got Title='{mergedIssue.Title}', TitleLastUpdate={mergedIssue.TitleLastUpdate}. " +
            $"Status='{mergedIssue.Status}', StatusLastUpdate={mergedIssue.StatusLastUpdate}");
        Assert.That(mergedIssue.Status, Is.EqualTo(IssueStatus.Progress), "Status from local should be preserved");
    }

    [Test]
    public async Task SyncAsync_NoLocalChanges_FastForwardsWithoutStash()
    {
        // Arrange: Clone A pushes changes
        var issue = CreateIssue("FFFFFF", "Issue from Clone A");
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add issue");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B has no local .fleece/ changes (just behind remote)

        // Act: Run sync on Clone B
        var syncService = CreateSyncService();
        var result = await syncService.SyncAsync(_cloneBPath, "main");

        // Assert: Sync should succeed (fast-forward)
        Assert.That(result.Success, Is.True, $"Sync failed: {result.ErrorMessage}");

        // Verify issue exists
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var finalStorage = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        var finalIssues = await finalStorage.LoadIssuesAsync(CancellationToken.None);

        Assert.That(finalIssues.Any(i => i.Id == "FFFFFF"), Is.True, "Issue should exist after sync");
    }

    [Test]
    public async Task SyncAsync_CommitMessageContainsSkipCi()
    {
        // Arrange: Clone A pushes changes
        var issue = CreateIssue("GGGGGG", "Issue from Clone A");
        await SaveAndCommitIssues(_cloneAPath, [issue], "Add issue");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone B creates local issue (uncommitted)
        var issueB = CreateIssue("HHHHHH", "Issue from Clone B");
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storageB = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        await storageB.SaveIssuesAsync([issue, issueB], CancellationToken.None);

        // Act: Run sync on Clone B
        var syncService = CreateSyncService();
        var result = await syncService.SyncAsync(_cloneBPath, "main");

        // Assert: Sync should succeed
        Assert.That(result.Success, Is.True, $"Sync failed: {result.ErrorMessage}");

        // Verify commit message contains [skip ci]
        var (_, lastCommitMessage, _) = RunGitInWithOutput(_cloneBPath, "log -1 --format=%s");
        Assert.That(lastCommitMessage.Trim(), Does.Contain("[skip ci]"),
            $"Commit message should contain [skip ci], but was: {lastCommitMessage}");
    }

    [Test]
    public async Task SyncAsync_MultipleConcurrentModifications_AllPreserved()
    {
        // Arrange: Create initial state
        await SaveAndCommitIssues(_cloneAPath, [], "Initialize empty fleece");
        RunGitIn(_cloneAPath, "push origin main");
        RunGitIn(_cloneBPath, "pull origin main");

        // Clone A creates issues X and Y, pushes
        var issueX = CreateIssue("XXXXXX", "Issue X");
        var issueY = CreateIssue("YYYYYY", "Issue Y");
        await SaveAndCommitIssues(_cloneAPath, [issueX, issueY], "Add X and Y");
        RunGitIn(_cloneAPath, "push origin main");

        // Clone B creates issues Z and W (uncommitted)
        var issueZ = CreateIssue("ZZZZZZ", "Issue Z");
        var issueW = CreateIssue("WWWWWW", "Issue W");
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storageB = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        await storageB.SaveIssuesAsync([issueZ, issueW], CancellationToken.None);

        // Act: Run sync on Clone B
        var syncService = CreateSyncService();
        var result = await syncService.SyncAsync(_cloneBPath, "main");

        // Assert: Sync should succeed and all 4 issues should exist
        Assert.That(result.Success, Is.True, $"Sync failed: {result.ErrorMessage}");

        var finalStorage = new JsonlStorageService(_cloneBPath, serializer, schemaValidator);
        var finalIssues = await finalStorage.LoadIssuesAsync(CancellationToken.None);

        Assert.That(finalIssues.Count, Is.EqualTo(4), "Should have all 4 issues");
        Assert.That(finalIssues.Any(i => i.Id == "XXXXXX"), Is.True, "Issue X should exist");
        Assert.That(finalIssues.Any(i => i.Id == "YYYYYY"), Is.True, "Issue Y should exist");
        Assert.That(finalIssues.Any(i => i.Id == "ZZZZZZ"), Is.True, "Issue Z should exist");
        Assert.That(finalIssues.Any(i => i.Id == "WWWWWW"), Is.True, "Issue W should exist");
    }

    private static (int exitCode, string output, string error) RunGitInWithOutput(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    private static FleeceIssuesSyncService CreateSyncService()
    {
        var mockGitHubEnvService = new Mock<IGitHubEnvironmentService>();
        mockGitHubEnvService
            .Setup(x => x.GetGitHubEnvironment())
            .Returns(new Dictionary<string, string>());

        var commandRunner = new CommandRunner(
            mockGitHubEnvService.Object,
            NullLogger<CommandRunner>.Instance);

        return new FleeceIssuesSyncService(
            commandRunner,
            NullLogger<FleeceIssuesSyncService>.Instance);
    }

    #endregion
}
