namespace Homespun.Shared.Models.Git;

/// <summary>
/// Information about a rebase conflict.
/// </summary>
public record RebaseConflict(string BranchName, string FeatureId, string ErrorMessage);
