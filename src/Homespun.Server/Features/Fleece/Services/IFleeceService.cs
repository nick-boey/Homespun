using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware service interface for Fleece issue tracking.
/// Wraps Fleece.Core's IIssueService to provide project path context.
/// </summary>
public interface IFleeceService
{
    #region Read Operations

    /// <summary>
    /// Gets a single issue by ID from the specified project.
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The issue, or null if not found.</returns>
    Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default);

    /// <summary>
    /// Lists issues from the specified project matching the filters.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="type">Optional type filter.</param>
    /// <param name="priority">Optional priority filter.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching issues.</returns>
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets issues that are ready to work on (open status with no blocking parent issues).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of ready issues.</returns>
    Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default);

    #endregion

    #region Cache Management

    /// <summary>
    /// Invalidates the in-memory cache and reloads all issues from disk for the specified project.
    /// Call this after external changes to .fleece/ files (e.g., git sync operations).
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="ct">Cancellation token</param>
    Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default);

    #endregion

    #region Task Graph Operations

    /// <summary>
    /// Builds a task graph for the specified project using Fleece.Core's TaskGraphService.
    /// The task graph organizes issues with actionable items at lane 0 (left) and
    /// parent/blocking issues at higher lanes (right).
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The task graph, or null if no issues exist.</returns>
    Task<TaskGraph?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default);

    #endregion

    #region Write Operations

    /// <summary>
    /// Creates a new issue in the specified project.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="title">Issue title.</param>
    /// <param name="type">Issue type.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="priority">Optional priority (1-5).</param>
    /// <param name="executionMode">Optional execution mode for child issues (defaults to Series).</param>
    /// <param name="status">Optional initial status (defaults to Open).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The created issue.</returns>
    Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        IssueStatus? status = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="title">Optional new title.</param>
    /// <param name="status">Optional new status.</param>
    /// <param name="type">Optional new type.</param>
    /// <param name="description">Optional new description.</param>
    /// <param name="priority">Optional new priority.</param>
    /// <param name="executionMode">Optional execution mode for child issues.</param>
    /// <param name="workingBranchId">Optional working branch ID.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated issue, or null if not found.</returns>
    Task<Issue?> UpdateIssueAsync(
        string projectPath,
        string issueId,
        string? title = null,
        IssueStatus? status = null,
        IssueType? type = null,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        string? workingBranchId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an issue (sets status to Deleted).
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the issue was found and deleted.</returns>
    Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default);

    #endregion
}
