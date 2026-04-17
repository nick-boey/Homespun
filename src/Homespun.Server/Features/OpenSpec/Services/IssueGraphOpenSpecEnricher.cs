using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.OpenSpec;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Wires the branch-state resolver into the issue graph response and projects scan
/// results onto the five-state graph indicator model.
/// </summary>
public class IssueGraphOpenSpecEnricher(
    IIssueBranchResolverService branchResolver,
    IBranchStateResolverService stateResolver,
    IDataStore dataStore,
    IGitCloneService cloneService,
    IChangeScannerService scanner,
    ILogger<IssueGraphOpenSpecEnricher> logger) : IIssueGraphOpenSpecEnricher
{
    /// <inheritdoc />
    public async Task EnrichAsync(
        string projectId,
        TaskGraphResponse response,
        CancellationToken ct = default)
    {
        foreach (var node in response.Nodes)
        {
            var issueId = node.Issue?.Id;
            if (string.IsNullOrWhiteSpace(issueId))
            {
                continue;
            }

            try
            {
                var state = await ResolveForIssueAsync(projectId, issueId, ct);
                if (state is not null)
                {
                    response.OpenSpecStates[issueId] = state;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to enrich OpenSpec state for issue {IssueId}", issueId);
            }
        }

        try
        {
            response.MainOrphanChanges = await ScanMainOrphansAsync(projectId, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to scan main-branch orphans for project {ProjectId}", projectId);
        }
    }

    internal async Task<IssueOpenSpecState?> ResolveForIssueAsync(
        string projectId,
        string issueId,
        CancellationToken ct)
    {
        var branch = await branchResolver.ResolveIssueBranchAsync(projectId, issueId);
        if (string.IsNullOrEmpty(branch))
        {
            return new IssueOpenSpecState
            {
                BranchState = BranchPresence.None,
                ChangeState = ChangePhase.None
            };
        }

        var snapshot = await stateResolver.GetOrScanAsync(projectId, branch, ct);
        if (snapshot is null || snapshot.Changes.Count == 0)
        {
            return new IssueOpenSpecState
            {
                BranchState = BranchPresence.Exists,
                ChangeState = ChangePhase.None,
                Orphans = snapshot?.Orphans ?? new List<SnapshotOrphan>()
            };
        }

        // Pick the most advanced linked change (archived > ready-to-archive > ready-to-apply > incomplete).
        var primary = snapshot.Changes
            .OrderByDescending(MapPhaseRank)
            .First();

        var phases = primary.Phases
            .Select(p => new PhaseSummary
            {
                Name = p.Name,
                Done = p.Done,
                Total = p.Total,
                Tasks = p.Tasks
                    .Select(t => new PhaseTaskSummary { Description = t.Description, Done = t.Done })
                    .ToList()
            })
            .ToList();

        return new IssueOpenSpecState
        {
            BranchState = BranchPresence.WithChange,
            ChangeState = MapPhase(primary),
            ChangeName = primary.Name,
            SchemaName = primary.ArtifactState?.SchemaName,
            Phases = phases,
            Orphans = snapshot.Orphans
        };
    }

    internal async Task<List<SnapshotOrphan>> ScanMainOrphansAsync(
        string projectId,
        CancellationToken ct)
    {
        var project = dataStore.GetProject(projectId);
        if (project is null || string.IsNullOrEmpty(project.LocalPath))
        {
            return new List<SnapshotOrphan>();
        }

        // Main orphans live in the project's primary repo clone (not a per-branch clone).
        var mainPath = project.LocalPath;
        if (!Directory.Exists(mainPath))
        {
            return new List<SnapshotOrphan>();
        }

        // Use a sentinel branch fleece-id that cannot match — all changes either become
        // "inherited" (sidecar points elsewhere) or orphans (no sidecar).
        const string SentinelFleeceId = "\0__homespun_main_scan__";
        var scan = await scanner.ScanBranchAsync(mainPath, SentinelFleeceId, baseBranch: null, ct);

        return scan.OrphanChanges
            .Select(o => new SnapshotOrphan { Name = o.Name, CreatedOnBranch = o.CreatedOnBranch })
            .ToList();
    }

    internal static ChangePhase MapPhase(SnapshotChange change)
    {
        if (change.IsArchived) return ChangePhase.Archived;
        if (change.TasksTotal > 0 && change.TasksDone >= change.TasksTotal) return ChangePhase.ReadyToArchive;
        if (change.ArtifactState?.IsComplete == true) return ChangePhase.ReadyToApply;
        return ChangePhase.Incomplete;
    }

    private static int MapPhaseRank(SnapshotChange change) => MapPhase(change) switch
    {
        ChangePhase.Archived => 4,
        ChangePhase.ReadyToArchive => 3,
        ChangePhase.ReadyToApply => 2,
        ChangePhase.Incomplete => 1,
        _ => 0
    };
}
