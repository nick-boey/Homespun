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
    /// </summary>
    Task EnrichAsync(
        string projectId,
        TaskGraphResponse response,
        CancellationToken ct = default);
}
