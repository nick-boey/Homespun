using System.Collections.Concurrent;
using Homespun.Features.PullRequests;
using Homespun.Shared.Models.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGitCloneService with in-memory clone tracking.
/// Optionally uses a real test working directory when live Claude sessions are enabled.
/// </summary>
public class MockGitCloneService : IGitCloneService
{
    private readonly ConcurrentDictionary<string, List<CloneInfo>> _clonesByRepo = new();
    private readonly ConcurrentDictionary<string, List<BranchInfo>> _branchesByRepo = new();
    private readonly ILogger<MockGitCloneService> _logger;
    private readonly LiveClaudeTestOptions? _liveTestOptions;
    private readonly OpenSpecMockSeeder? _openSpecSeeder;

    public MockGitCloneService(
        ILogger<MockGitCloneService> logger,
        IOptions<LiveClaudeTestOptions>? liveTestOptions = null,
        OpenSpecMockSeeder? openSpecSeeder = null)
    {
        _logger = logger;
        _liveTestOptions = liveTestOptions?.Value;
        _openSpecSeeder = openSpecSeeder;
    }

    /// <summary>
    /// Gets the test working directory if live Claude sessions are enabled.
    /// </summary>
    public string? TestWorkingDirectory => _liveTestOptions?.TestWorkingDirectory;

    public async Task<string?> CreateCloneAsync(
        string repoPath,
        string branchName,
        bool createBranch = false,
        string? baseBranch = null)
    {
        _logger.LogDebug("[Mock] CreateClone {BranchName} in {RepoPath} from base {BaseBranch}", branchName, repoPath, baseBranch ?? "HEAD");

        var isLiveMode = !string.IsNullOrEmpty(_liveTestOptions?.TestWorkingDirectory);

        // If live Claude testing is enabled, use the real test directory
        var clonePath = isLiveMode
            ? _liveTestOptions!.TestWorkingDirectory
            : $"{repoPath}-clones/{branchName.Replace("/", "-")}";

        if (!isLiveMode)
        {
            await MaterializeCloneAsync(repoPath, clonePath, branchName);
        }

        var clones = _clonesByRepo.GetOrAdd(repoPath, _ => []);
        lock (clones)
        {
            clones.Add(new CloneInfo
            {
                Path = clonePath,
                Branch = branchName,
                HeadCommit = Guid.NewGuid().ToString("N")[..7],
                IsBare = false,
                IsDetached = false
            });
        }

        // Also track the branch
        var branches = _branchesByRepo.GetOrAdd(repoPath, _ => [new BranchInfo { Name = "main", ShortName = "main", IsCurrent = true }]);
        lock (branches)
        {
            if (!branches.Any(b => b.ShortName == branchName))
            {
                branches.Add(new BranchInfo
                {
                    Name = branchName,
                    ShortName = branchName,
                    IsCurrent = false,
                    HasClone = true,
                    ClonePath = clonePath
                });
            }
        }

        return clonePath;
    }

    /// <summary>
    /// Creates the clone directory on disk, copies the parent project's <c>openspec/</c>
    /// and <c>.fleece/</c> subtrees into it, then asks the OpenSpec seeder to apply any
    /// branch-specific deltas. Gated on <c>LiveClaudeTestOptions.TestWorkingDirectory</c>
    /// being empty — the live-Claude profiles share a single workspace and must not
    /// get per-branch directories.
    /// </summary>
    private async Task MaterializeCloneAsync(string repoPath, string clonePath, string branchName)
    {
        Directory.CreateDirectory(clonePath);
        CopyDirectory(Path.Combine(repoPath, "openspec"), Path.Combine(clonePath, "openspec"));
        CopyDirectory(Path.Combine(repoPath, ".fleece"), Path.Combine(clonePath, ".fleece"));

        if (_openSpecSeeder is not null)
        {
            var fleeceId = BranchNameParser.ExtractIssueId(branchName);
            await _openSpecSeeder.SeedBranchAsync(clonePath, branchName, fleeceId);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var subdir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subdir, Path.Combine(destDir, Path.GetFileName(subdir)));
        }
    }

    public Task<bool> RemoveCloneAsync(string repoPath, string clonePath)
    {
        _logger.LogDebug("[Mock] RemoveClone {ClonePath} from {RepoPath}", clonePath, repoPath);

        if (_clonesByRepo.TryGetValue(repoPath, out var clones))
        {
            lock (clones)
            {
                var removed = clones.RemoveAll(w => w.Path == clonePath) > 0;
                return Task.FromResult(removed);
            }
        }

        return Task.FromResult(false);
    }

    public Task<List<CloneInfo>> ListClonesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListClones in {RepoPath}", repoPath);

        if (_clonesByRepo.TryGetValue(repoPath, out var clones))
        {
            lock (clones)
            {
                return Task.FromResult(clones.ToList());
            }
        }

        // Return default clone (the main repo)
        return Task.FromResult(new List<CloneInfo>
        {
            new()
            {
                Path = repoPath,
                Branch = "main",
                HeadCommit = "abc1234",
                IsBare = false,
                IsDetached = false
            }
        });
    }

    public Task PruneClonesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] PruneClones in {RepoPath}", repoPath);
        return Task.CompletedTask;
    }

    public Task<bool> CloneExistsAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] CloneExists {BranchName} in {RepoPath}", branchName, repoPath);

        if (_clonesByRepo.TryGetValue(repoPath, out var clones))
        {
            lock (clones)
            {
                return Task.FromResult(clones.Any(w => w.Branch == branchName));
            }
        }

        return Task.FromResult(false);
    }

    public Task<string?> GetClonePathForBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] GetClonePathForBranch {BranchName} in {RepoPath}", branchName, repoPath);

        // If live Claude testing is enabled, always return the real test directory
        if (!string.IsNullOrEmpty(_liveTestOptions?.TestWorkingDirectory))
        {
            return Task.FromResult<string?>(_liveTestOptions.TestWorkingDirectory);
        }

        if (_clonesByRepo.TryGetValue(repoPath, out var clones))
        {
            lock (clones)
            {
                var clone = clones.FirstOrDefault(w => w.Branch == branchName);
                return Task.FromResult(clone?.Path);
            }
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> PullLatestAsync(string clonePath)
    {
        _logger.LogDebug("[Mock] PullLatest in {ClonePath}", clonePath);
        return Task.FromResult(true);
    }

    public Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] FetchAndUpdateBranch {BranchName} in {RepoPath}", branchName, repoPath);
        return Task.FromResult(true);
    }

    public Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListLocalBranches in {RepoPath}", repoPath);

        if (_branchesByRepo.TryGetValue(repoPath, out var branches))
        {
            lock (branches)
            {
                return Task.FromResult(branches.ToList());
            }
        }

        // Return default branches
        return Task.FromResult(new List<BranchInfo>
        {
            new()
            {
                Name = "main",
                ShortName = "main",
                IsCurrent = true,
                CommitSha = "abc1234567890",
                Upstream = "origin/main"
            }
        });
    }

    public Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] ListRemoteOnlyBranches in {RepoPath}", repoPath);
        return Task.FromResult(new List<string>());
    }

    public Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        _logger.LogDebug("[Mock] IsBranchMerged {BranchName} into {TargetBranch} in {RepoPath}",
            branchName, targetBranch, repoPath);
        return Task.FromResult(false);
    }

    public Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false)
    {
        _logger.LogDebug("[Mock] DeleteLocalBranch {BranchName} in {RepoPath}", branchName, repoPath);

        if (_branchesByRepo.TryGetValue(repoPath, out var branches))
        {
            lock (branches)
            {
                branches.RemoveAll(b => b.ShortName == branchName);
            }
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] DeleteRemoteBranch {BranchName} in {RepoPath}", branchName, repoPath);
        return Task.FromResult(true);
    }

    public Task<bool> RemoteBranchExistsAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] RemoteBranchExists {BranchName} in {RepoPath}", branchName, repoPath);
        // In mock, always return true (assume remote exists) for testing scenarios
        return Task.FromResult(true);
    }

    public Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch)
    {
        _logger.LogDebug("[Mock] CreateLocalBranchFromRemote {RemoteBranch} in {RepoPath}", remoteBranch, repoPath);

        var localBranchName = remoteBranch.Replace("origin/", "");
        var branches = _branchesByRepo.GetOrAdd(repoPath, _ => []);
        lock (branches)
        {
            branches.Add(new BranchInfo
            {
                Name = localBranchName,
                ShortName = localBranchName,
                IsCurrent = false,
                Upstream = remoteBranch
            });
        }

        return Task.FromResult(true);
    }

    public Task<(int ahead, int behind)> GetBranchDivergenceAsync(
        string repoPath,
        string branchName,
        string targetBranch)
    {
        _logger.LogDebug("[Mock] GetBranchDivergence {BranchName} vs {TargetBranch} in {RepoPath}",
            branchName, targetBranch, repoPath);
        return Task.FromResult((ahead: 1, behind: 0));
    }

    public Task<bool> FetchAllAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] FetchAll in {RepoPath}", repoPath);
        return Task.FromResult(true);
    }

    public Task<CloneStatus> GetCloneStatusAsync(string clonePath)
    {
        _logger.LogDebug("[Mock] GetCloneStatus in {ClonePath}", clonePath);
        return Task.FromResult(new CloneStatus
        {
            ModifiedCount = 0,
            StagedCount = 0,
            UntrackedCount = 0
        });
    }

    public Task<List<LostCloneInfo>> FindLostCloneFoldersAsync(string repoPath)
    {
        _logger.LogDebug("[Mock] FindLostCloneFolders in {RepoPath}", repoPath);
        return Task.FromResult(new List<LostCloneInfo>());
    }

    public Task<bool> DeleteCloneFolderAsync(string folderPath)
    {
        _logger.LogDebug("[Mock] DeleteCloneFolder {FolderPath}", folderPath);
        return Task.FromResult(true);
    }

    public Task<string?> GetCurrentBranchAsync(string clonePath)
    {
        _logger.LogDebug("[Mock] GetCurrentBranch in {ClonePath}", clonePath);

        // Try to find the clone and return its branch
        foreach (var clones in _clonesByRepo.Values)
        {
            lock (clones)
            {
                var clone = clones.FirstOrDefault(w => w.Path == clonePath);
                if (clone != null)
                {
                    return Task.FromResult<string?>(clone.Branch?.Replace("refs/heads/", ""));
                }
            }
        }

        return Task.FromResult<string?>("main");
    }

    public Task<bool> CheckoutBranchAsync(string clonePath, string branchName)
    {
        _logger.LogDebug("[Mock] CheckoutBranch {BranchName} in {ClonePath}", branchName, clonePath);

        // Update the clone's branch
        foreach (var clones in _clonesByRepo.Values)
        {
            lock (clones)
            {
                var clone = clones.FirstOrDefault(w => w.Path == clonePath);
                if (clone != null)
                {
                    clone.Branch = branchName;
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(true);
    }

    public Task<bool> IsSquashMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        _logger.LogDebug("[Mock] IsSquashMerged {BranchName} into {TargetBranch} in {RepoPath}",
            branchName, targetBranch, repoPath);
        return Task.FromResult(false);
    }

    public async Task<string?> CreateCloneFromRemoteBranchAsync(string repoPath, string remoteBranch)
    {
        _logger.LogDebug("[Mock] CreateCloneFromRemoteBranch {RemoteBranch} in {RepoPath}",
            remoteBranch, repoPath);

        var isLiveMode = !string.IsNullOrEmpty(_liveTestOptions?.TestWorkingDirectory);

        var clonePath = isLiveMode
            ? _liveTestOptions!.TestWorkingDirectory
            : $"{repoPath}-clones/{remoteBranch.Replace("/", "-")}";

        if (!isLiveMode)
        {
            await MaterializeCloneAsync(repoPath, clonePath, remoteBranch);
        }

        var clones = _clonesByRepo.GetOrAdd(repoPath, _ => []);
        lock (clones)
        {
            clones.Add(new CloneInfo
            {
                Path = clonePath,
                Branch = remoteBranch,
                HeadCommit = Guid.NewGuid().ToString("N")[..7],
                IsBare = false,
                IsDetached = false
            });
        }

        // Track the branch
        var branches = _branchesByRepo.GetOrAdd(repoPath, _ => []);
        lock (branches)
        {
            if (!branches.Any(b => b.ShortName == remoteBranch))
            {
                branches.Add(new BranchInfo
                {
                    Name = remoteBranch,
                    ShortName = remoteBranch,
                    IsCurrent = false,
                    HasClone = true,
                    ClonePath = clonePath,
                    Upstream = $"origin/{remoteBranch}"
                });
            }
        }

        return clonePath;
    }

    public Task<bool> RepairCloneAsync(string repoPath, string folderPath, string branchName)
    {
        _logger.LogDebug("[Mock] RepairClone {FolderPath} for branch {BranchName} in {RepoPath}",
            folderPath, branchName, repoPath);

        // Add the folder as a clone
        var clones = _clonesByRepo.GetOrAdd(repoPath, _ => []);
        lock (clones)
        {
            clones.Add(new CloneInfo
            {
                Path = folderPath,
                Branch = branchName,
                HeadCommit = Guid.NewGuid().ToString("N")[..7],
                IsBare = false,
                IsDetached = false
            });
        }

        return Task.FromResult(true);
    }

    public Task<List<FileChangeInfo>> GetChangedFilesAsync(string clonePath, string targetBranch)
    {
        _logger.LogDebug("[Mock] GetChangedFilesAsync in {ClonePath} against {TargetBranch}", clonePath, targetBranch);
        // Return empty list by default for mock
        return Task.FromResult(new List<FileChangeInfo>());
    }

    public Task<SessionBranchInfo?> GetSessionBranchInfoAsync(string clonePath)
    {
        _logger.LogDebug("[Mock] GetSessionBranchInfoAsync in {ClonePath}", clonePath);

        // Try to find the clone and return its branch info
        foreach (var clones in _clonesByRepo.Values)
        {
            lock (clones)
            {
                var clone = clones.FirstOrDefault(w => w.Path == clonePath);
                if (clone != null)
                {
                    return Task.FromResult<SessionBranchInfo?>(new SessionBranchInfo
                    {
                        BranchName = clone.Branch?.Replace("refs/heads/", ""),
                        CommitSha = clone.HeadCommit?[..7],
                        CommitMessage = "Mock commit message",
                        CommitDate = DateTime.UtcNow,
                        AheadCount = 1,
                        BehindCount = 0,
                        HasUncommittedChanges = false
                    });
                }
            }
        }

        // Return a default session branch info
        return Task.FromResult<SessionBranchInfo?>(new SessionBranchInfo
        {
            BranchName = "main",
            CommitSha = "abc1234",
            CommitMessage = "Mock commit",
            CommitDate = DateTime.UtcNow,
            AheadCount = 0,
            BehindCount = 0,
            HasUncommittedChanges = false
        });
    }

    public Task EnsureBranchAvailableAsync(string repoPath, string branchName)
    {
        _logger.LogDebug("[Mock] EnsureBranchAvailable {BranchName} in {RepoPath}", branchName, repoPath);
        // In mock, this is a no-op - branches are always considered available
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears all tracked data. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        _clonesByRepo.Clear();
        _branchesByRepo.Clear();
    }
}
