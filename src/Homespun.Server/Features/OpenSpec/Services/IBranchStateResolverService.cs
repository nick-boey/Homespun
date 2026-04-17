using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Resolves the <see cref="BranchStateSnapshot"/> for a <c>(projectId, branch)</c> pair by
/// either returning a fresh cached entry or performing a live on-disk scan against the
/// branch's clone, caching the result.
/// </summary>
public interface IBranchStateResolverService
{
    /// <summary>
    /// Returns a snapshot for the branch. Performs an on-disk scan of the clone when no
    /// cached entry exists or the cached entry has expired, and caches the fresh result.
    /// Returns null when the project, branch fleece-id, or clone cannot be resolved.
    /// </summary>
    Task<BranchStateSnapshot?> GetOrScanAsync(
        string projectId,
        string branch,
        CancellationToken ct = default);
}
