using Homespun.Shared.Models.GitHub;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for resolving the final status of removed PRs.
/// When PRs are removed from local tracking (no longer open on GitHub),
/// this service fetches their actual status (merged or closed) and updates
/// the graph cache accordingly.
/// </summary>
public interface IPRStatusResolver
{
    /// <summary>
    /// Resolves the final status (merged/closed) for PRs that were removed from local tracking.
    /// Fetches individual PR status from GitHub and updates the graph cache.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="removedPrs">List of PRs that were removed from local tracking.</param>
    Task ResolveClosedPRStatusesAsync(string projectId, List<RemovedPrInfo> removedPrs);
}
