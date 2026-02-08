using System.Collections.Concurrent;
using Homespun.Features.Commands;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Manages per-issue workspaces with isolated .claude, .sessions, and src directories.
/// Folder hierarchy:
///   {baseDir}/{projectName}/main/                     - Main branch clone
///   {baseDir}/{projectName}/issues/{issueId}/.claude/  - Claude Code state
///   {baseDir}/{projectName}/issues/{issueId}/.sessions/ - JSONL message cache
///   {baseDir}/{projectName}/issues/{issueId}/src/       - Git clone for agent work
/// </summary>
public class IssueWorkspaceService : IIssueWorkspaceService
{
    private readonly string _baseDir;
    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<IssueWorkspaceService> _logger;
    private readonly ConcurrentDictionary<string, IssueWorkspace> _workspaces = new();

    public IssueWorkspaceService(
        string baseDir,
        ICommandRunner commandRunner,
        ILogger<IssueWorkspaceService> logger)
    {
        _baseDir = baseDir;
        _commandRunner = commandRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureProjectSetupAsync(
        string projectId,
        string projectName,
        string repoUrl,
        string defaultBranch,
        CancellationToken ct = default)
    {
        var mainDir = Path.Combine(_baseDir, projectName, "main");

        if (Directory.Exists(Path.Combine(mainDir, ".git")))
        {
            _logger.LogDebug("Project {ProjectName} main clone exists, pulling latest", projectName);
            await _commandRunner.RunAsync("git", "pull", mainDir);
            return;
        }

        Directory.CreateDirectory(mainDir);
        _logger.LogInformation("Cloning {RepoUrl} to {MainDir} for project {ProjectName}", repoUrl, mainDir, projectName);
        var result = await _commandRunner.RunAsync("git", $"clone {repoUrl} {mainDir}", _baseDir);
        if (!result.Success)
        {
            _logger.LogError("Failed to clone repository: {Error}", result.Error);
            throw new InvalidOperationException($"Failed to clone repository {repoUrl}: {result.Error}");
        }
    }

    /// <inheritdoc />
    public async Task<IssueWorkspace> EnsureIssueWorkspaceAsync(
        string projectName,
        string issueId,
        string repoUrl,
        string branchName,
        CancellationToken ct = default)
    {
        var issueDir = GetIssueDir(projectName, issueId);
        var claudePath = Path.Combine(issueDir, ".claude");
        var sessionsPath = Path.Combine(issueDir, ".sessions");
        var sourcePath = Path.Combine(issueDir, "src");

        // Create directory structure
        Directory.CreateDirectory(claudePath);
        Directory.CreateDirectory(sessionsPath);
        Directory.CreateDirectory(issueDir); // Ensure parent of src exists

        // Clone if src doesn't have a .git directory
        if (!Directory.Exists(Path.Combine(sourcePath, ".git")))
        {
            _logger.LogInformation("Cloning {RepoUrl} to {SourcePath} for issue {IssueId}", repoUrl, sourcePath, issueId);
            var cloneResult = await _commandRunner.RunAsync("git", $"clone {repoUrl} {sourcePath}", issueDir);
            if (!cloneResult.Success)
            {
                _logger.LogError("Failed to clone repository for issue {IssueId}: {Error}", issueId, cloneResult.Error);
                throw new InvalidOperationException($"Failed to clone repository for issue {issueId}: {cloneResult.Error}");
            }
        }

        // Checkout branch (try existing branch first, then create new)
        var checkoutResult = await _commandRunner.RunAsync("git", $"checkout {branchName}", sourcePath);
        if (!checkoutResult.Success)
        {
            _logger.LogDebug("Branch {BranchName} doesn't exist, creating new branch", branchName);
            var createResult = await _commandRunner.RunAsync("git", $"checkout -b {branchName}", sourcePath);
            if (!createResult.Success)
            {
                _logger.LogError("Failed to create branch {BranchName}: {Error}", branchName, createResult.Error);
                throw new InvalidOperationException($"Failed to create branch {branchName}: {createResult.Error}");
            }
        }

        var workspace = new IssueWorkspace(issueId, claudePath, sessionsPath, sourcePath, branchName);
        _workspaces[WorkspaceKey(projectName, issueId)] = workspace;
        return workspace;
    }

    /// <inheritdoc />
    public IssueWorkspace? GetIssueWorkspace(string projectName, string issueId)
    {
        var key = WorkspaceKey(projectName, issueId);

        // Check in-memory cache first
        if (_workspaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Fall back to filesystem detection
        var issueDir = GetIssueDir(projectName, issueId);
        if (!Directory.Exists(issueDir))
        {
            return null;
        }

        var claudePath = Path.Combine(issueDir, ".claude");
        var sessionsPath = Path.Combine(issueDir, ".sessions");
        var sourcePath = Path.Combine(issueDir, "src");

        if (!Directory.Exists(claudePath) || !Directory.Exists(sessionsPath))
        {
            return null;
        }

        var branchName = DetectBranchName(sourcePath);
        var workspace = new IssueWorkspace(issueId, claudePath, sessionsPath, sourcePath, branchName ?? "unknown");
        _workspaces[key] = workspace;
        return workspace;
    }

    /// <inheritdoc />
    public Task CleanupIssueWorkspaceAsync(
        string projectName,
        string issueId,
        CancellationToken ct = default)
    {
        _workspaces.TryRemove(WorkspaceKey(projectName, issueId), out _);

        var issueDir = GetIssueDir(projectName, issueId);
        if (Directory.Exists(issueDir))
        {
            _logger.LogInformation("Cleaning up workspace for issue {IssueId} at {IssueDir}", issueId, issueDir);
            Directory.Delete(issueDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string GetIssueDir(string projectName, string issueId)
        => Path.Combine(_baseDir, projectName, "issues", issueId);

    private static string WorkspaceKey(string projectName, string issueId)
        => $"{projectName}:{issueId}";

    private string? DetectBranchName(string sourcePath)
    {
        try
        {
            var headFile = Path.Combine(sourcePath, ".git", "HEAD");
            if (!File.Exists(headFile)) return null;

            var content = File.ReadAllText(headFile).Trim();
            // HEAD file contains "ref: refs/heads/<branch-name>"
            const string refPrefix = "ref: refs/heads/";
            return content.StartsWith(refPrefix) ? content[refPrefix.Length..] : null;
        }
        catch
        {
            return null;
        }
    }
}
