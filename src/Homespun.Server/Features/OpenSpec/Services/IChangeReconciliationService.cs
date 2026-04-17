using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Wraps <see cref="IChangeScannerService"/> with side-effectful reconciliation:
/// auto-writes sidecars for unambiguous orphans and auto-transitions Fleece issues
/// to <c>complete</c> when a linked change has been archived.
/// </summary>
public interface IChangeReconciliationService
{
    /// <summary>
    /// Scans the branch clone and applies reconciliation:
    /// <list type="bullet">
    ///   <item>Auto-writes sidecar when exactly one orphan change exists.</item>
    ///   <item>Transitions the Fleece issue to <c>complete</c> when any linked change is archived
    ///   (and the issue is not already complete/closed).</item>
    /// </list>
    /// </summary>
    /// <param name="projectId">The project the branch belongs to.</param>
    /// <param name="clonePath">Absolute path to the clone root.</param>
    /// <param name="branchFleeceId">The fleece-id suffix parsed from the branch name.</param>
    /// <param name="baseBranch">Optional base branch used for orphan "created on branch" detection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The post-reconciliation scan result (includes any orphan that was just auto-linked).</returns>
    Task<BranchScanResult> ReconcileAsync(
        string projectId,
        string clonePath,
        string branchFleeceId,
        string? baseBranch = null,
        CancellationToken ct = default);
}
