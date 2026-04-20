using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Enriches a <see cref="TaskGraphResponse"/> with OpenSpec state for each issue:
/// populates <c>OpenSpecStates</c> and surfaces any main-branch orphans.
/// </summary>
public interface IIssueGraphOpenSpecEnricher
{
    /// <summary>
    /// Populates <c>OpenSpecStates</c> and <c>MainOrphanChanges</c> on the given response.
    /// Swallows errors per-issue and logs at debug level — the graph must render regardless.
    ///
    /// <paramref name="branchContext"/> is optional — when omitted the enricher
    /// builds a one-off context, paying a single <see cref="IGitCloneService.ListClonesAsync"/>
    /// + a single <see cref="IDataStore.GetPullRequestsByProject"/> instead of
    /// re-scanning per visible node.
    /// </summary>
    Task EnrichAsync(
        string projectId,
        TaskGraphResponse response,
        BranchResolutionContext? branchContext = null,
        CancellationToken ct = default);
}
