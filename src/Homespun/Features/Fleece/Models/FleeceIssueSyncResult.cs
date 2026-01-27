namespace Homespun.Features.Fleece.Models;

/// <summary>
/// Result of a fleece issues sync operation (commit and push).
/// </summary>
public record FleeceIssueSyncResult(
    bool Success,
    string? ErrorMessage,
    int FilesCommitted,
    bool PushSucceeded);

/// <summary>
/// Result of a pull operation.
/// </summary>
public record PullResult(
    bool Success,
    bool HasConflicts,
    string? ErrorMessage);
