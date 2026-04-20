using Homespun.Shared.Models.Git;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Per-request snapshot of the inputs <see cref="IssueBranchResolverService"/>
/// needs to resolve issue → branch mappings without fanning out subprocess calls.
/// Built once per request (single <see cref="IGitCloneService.ListClonesAsync"/>
/// + single <see cref="IDataStore.GetPullRequestsByProject"/>) and passed down
/// through the enrichment chain.
/// </summary>
public sealed record BranchResolutionContext(
    IReadOnlyList<CloneInfo> Clones,
    IReadOnlyDictionary<string, string> PrBranchByIssueId);
