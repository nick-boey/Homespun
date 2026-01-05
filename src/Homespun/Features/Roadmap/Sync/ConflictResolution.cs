namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Resolution option for a worktree ROADMAP.json conflict.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Keep the existing ROADMAP.json in the worktree.
    /// The worktree's changes will be preserved and reviewed in the PR.
    /// </summary>
    KeepExisting,

    /// <summary>
    /// Overwrite with the new ROADMAP.local.json content.
    /// The worktree's changes will be lost.
    /// </summary>
    UseNew
}
