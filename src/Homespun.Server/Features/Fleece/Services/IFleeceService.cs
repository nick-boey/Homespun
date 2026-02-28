using Fleece.Core.Models;
using Homespun.Shared.Requests;

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

    /// <summary>
    /// Builds a task graph for the specified project, including additional issues by ID
    /// regardless of their status. This is used to ensure issues linked to open PRs
    /// are included in the graph even if they have a terminal status (Complete, Closed, etc.).
    /// </summary>
    /// <param name="projectPath">Path to the project containing .fleece/ directory</param>
    /// <param name="additionalIssueIds">Issue IDs to include regardless of status.
    /// These issues will be included in the graph even if their status would normally exclude them.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The task graph, or null if no issues exist.</returns>
    Task<TaskGraph?> GetTaskGraphWithAdditionalIssuesAsync(
        string projectPath,
        IEnumerable<string>? additionalIssueIds,
        CancellationToken ct = default);

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

    /// <summary>
    /// Adds a parent relationship to an issue. The child issue will be blocked by the parent.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue that will have the parent added.</param>
    /// <param name="parentId">The ID of the parent issue to add.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue with the new parent relationship.</returns>
    /// <exception cref="KeyNotFoundException">If the child issue is not found.</exception>
    Task<Issue> AddParentAsync(string projectPath, string childId, string parentId, string? sortOrder = null, CancellationToken ct = default);

    /// <summary>
    /// Removes a parent relationship from an issue.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue that will have the parent removed.</param>
    /// <param name="parentId">The ID of the parent issue to remove.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue with the parent relationship removed.</returns>
    /// <exception cref="KeyNotFoundException">If the child issue is not found.</exception>
    Task<Issue> RemoveParentAsync(string projectPath, string childId, string parentId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether setting a parent relationship would create a cycle.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the issue that would become the child.</param>
    /// <param name="parentId">The ID of the issue that would become the parent.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the relationship would create a cycle, false otherwise.</returns>
    Task<bool> WouldCreateCycleAsync(string projectPath, string childId, string parentId, CancellationToken ct = default);

    /// <summary>
    /// Sets the parent of an issue, optionally replacing all existing parents.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="childId">The ID of the child issue.</param>
    /// <param name="parentId">The ID of the new parent issue.</param>
    /// <param name="addToExisting">If true, adds to existing parents; if false, replaces all existing parents.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated child issue.</returns>
    /// <exception cref="KeyNotFoundException">If the child issue is not found.</exception>
    /// <exception cref="InvalidOperationException">If the relationship would create a cycle.</exception>
    Task<Issue> SetParentAsync(string projectPath, string childId, string parentId, bool addToExisting = false, CancellationToken ct = default);

    /// <summary>
    /// Moves a series sibling issue up or down by swapping its sort order with the adjacent sibling.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issueId">The issue ID to move.</param>
    /// <param name="direction">Direction to move (Up or Down).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated issue with its new sort order.</returns>
    /// <exception cref="KeyNotFoundException">If the issue is not found.</exception>
    /// <exception cref="InvalidOperationException">If the issue has no parent, multiple parents, or is already first/last.</exception>
    Task<Issue> MoveSeriesSiblingAsync(string projectPath, string issueId, MoveDirection direction, CancellationToken ct = default);

    #endregion

    #region History Operations

    /// <summary>
    /// Applies issues from a history snapshot to the cache and persists to disk.
    /// Used by undo/redo operations.
    /// </summary>
    /// <param name="projectPath">Path to the project.</param>
    /// <param name="issues">The issues to apply from the snapshot.</param>
    /// <param name="ct">Cancellation token</param>
    Task ApplyHistorySnapshotAsync(string projectPath, IReadOnlyList<Issue> issues, CancellationToken ct = default);

    #endregion
}
