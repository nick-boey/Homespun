using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for synchronizing fleece issues with the remote repository.
/// </summary>
public interface IFleeceIssuesSyncService
{
    /// <summary>
    /// Checks the current branch status against the expected default branch.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="defaultBranch">The expected default branch name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating if we're on the correct branch and behind/ahead status.</returns>
    Task<BranchStatusResult> CheckBranchStatusAsync(string projectPath, string defaultBranch, CancellationToken ct = default);

    /// <summary>
    /// Commits all files in .fleece/ folder and pushes to the default branch.
    /// This method will automatically try to pull from remote if behind.
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

    /// <summary>
    /// Discards only non-.fleece/ changes while keeping .fleece/ changes intact.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if discard was successful.</returns>
    Task<bool> DiscardNonFleeceChangesAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Pulls fleece issues from the remote repository without committing or pushing.
    /// This operation fetches from remote, fast-forwards if behind, and merges fleece issues locally.
    /// </summary>
    /// <param name="projectPath">The local path of the project.</param>
    /// <param name="defaultBranch">The default branch name to pull from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success/failure and number of issues merged.</returns>
    Task<FleecePullResult> PullFleeceOnlyAsync(string projectPath, string defaultBranch, CancellationToken ct = default);
}
