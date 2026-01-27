using Homespun.Features.Fleece.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for synchronizing fleece issues with the remote repository.
/// </summary>
public interface IFleeceIssuesSyncService
{
    /// <summary>
    /// Commits all files in .fleece/ folder and pushes to the default branch.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="defaultBranch">The default branch name to push to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success/failure and number of files committed.</returns>
    Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default);

    /// <summary>
    /// Pulls changes from the default branch.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="defaultBranch">The default branch name to pull from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success/failure and whether conflicts occurred.</returns>
    Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default);

    /// <summary>
    /// Stashes local changes.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if stash was successful.</returns>
    Task<bool> StashChangesAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Discards all local changes (staged and unstaged).
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if discard was successful.</returns>
    Task<bool> DiscardChangesAsync(string projectPath, CancellationToken ct = default);
}
