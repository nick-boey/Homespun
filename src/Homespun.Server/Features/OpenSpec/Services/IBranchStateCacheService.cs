using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// In-memory cache of per-branch OpenSpec state snapshots, keyed by <c>(projectId, branch)</c>.
/// Entries expire after a configurable TTL.
/// </summary>
public interface IBranchStateCacheService
{
    /// <summary>
    /// The cache TTL. Entries older than this are treated as missing.
    /// </summary>
    TimeSpan Ttl { get; }

    /// <summary>
    /// Stores a snapshot, overwriting any existing entry for the same key.
    /// </summary>
    void Put(BranchStateSnapshot snapshot);

    /// <summary>
    /// Retrieves a snapshot if one exists and has not expired.
    /// </summary>
    BranchStateSnapshot? TryGet(string projectId, string branch);

    /// <summary>
    /// Drops any cached snapshot for the given key.
    /// </summary>
    void Invalidate(string projectId, string branch);
}
