using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Pure parent-chain traversal that returns every issue reachable from a seed
/// set by walking <see cref="Issue.ParentIssues"/> references. Cycle-safe.
/// </summary>
public interface IIssueAncestorTraversalService
{
    /// <summary>
    /// Returns every issue in <paramref name="issues"/> whose id is in
    /// <paramref name="seedIds"/> or is reachable from a seed by walking
    /// parent references. Seeds that don't resolve to an issue in the input
    /// are skipped silently. Parent refs pointing to ids not in the input
    /// are ignored. Cycles in the parent chain are handled by a visited set.
    /// </summary>
    IReadOnlyCollection<Issue> CollectVisible(
        IReadOnlyList<Issue> issues,
        IReadOnlySet<string> seedIds);
}
