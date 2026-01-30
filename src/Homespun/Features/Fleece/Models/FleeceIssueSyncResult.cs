namespace Homespun.Features.Fleece.Models;

/// <summary>
/// Result of a fleece issues sync operation (commit and push).
/// </summary>
public record FleeceIssueSyncResult(
    bool Success,
    string? ErrorMessage,
    int FilesCommitted,
    bool PushSucceeded,
    bool RequiresPullFirst = false,
    bool HasNonFleeceChanges = false,
    IReadOnlyList<string>? NonFleeceChangedFiles = null);

/// <summary>
/// Result of a pull operation.
/// </summary>
public record PullResult(
    bool Success,
    bool HasConflicts,
    string? ErrorMessage,
    bool HasNonFleeceChanges = false,
    IReadOnlyList<string>? NonFleeceChangedFiles = null);

/// <summary>
/// Result of a branch status check.
/// </summary>
public record BranchStatusResult(
    bool Success,
    bool IsOnCorrectBranch,
    string? CurrentBranch,
    string? ErrorMessage,
    bool IsBehindRemote,
    int CommitsBehind,
    int CommitsAhead);
