namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Represents the workspace paths for a specific issue.
/// </summary>
public record IssueWorkspace(
    string IssueId,
    string ClaudePath,      // /home/projects/{name}/issues/{id}/.claude/
    string SessionsPath,    // /home/projects/{name}/issues/{id}/.sessions/
    string SourcePath,      // /home/projects/{name}/issues/{id}/src/
    string BranchName
);

/// <summary>
/// Manages per-issue workspaces with isolated .claude, .sessions, and src directories.
/// </summary>
public interface IIssueWorkspaceService
{
    /// <summary>
    /// Ensures the project's main branch clone exists and is up-to-date.
    /// </summary>
    Task EnsureProjectSetupAsync(
        string projectId,
        string projectName,
        string repoUrl,
        string defaultBranch,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or verifies the per-issue folder structure (clone, .claude, .sessions).
    /// Creates and checks out the issue branch if needed.
    /// </summary>
    Task<IssueWorkspace> EnsureIssueWorkspaceAsync(
        string projectName,
        string issueId,
        string repoUrl,
        string branchName,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the workspace paths for an existing issue.
    /// Returns null if the workspace doesn't exist.
    /// </summary>
    IssueWorkspace? GetIssueWorkspace(string projectName, string issueId);

    /// <summary>
    /// Cleans up the workspace for a completed/archived issue.
    /// </summary>
    Task CleanupIssueWorkspaceAsync(
        string projectName,
        string issueId,
        CancellationToken ct = default);
}
