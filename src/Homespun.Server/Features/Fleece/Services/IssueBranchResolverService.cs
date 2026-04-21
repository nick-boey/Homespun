using Homespun.Features.Git;
using Homespun.Features.OpenSpec.Telemetry;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for resolving the branch name for an issue by checking linked PRs, working branch IDs, and existing clones.
/// </summary>
public class IssueBranchResolverService(
    IDataStore dataStore,
    IGitCloneService gitCloneService,
    IProjectFleeceService fleeceService,
    ILogger<IssueBranchResolverService> logger) : IIssueBranchResolverService
{
    /// <inheritdoc />
    public async Task<string?> ResolveIssueBranchAsync(string projectId, string issueId)
    {
        // Legacy single-issue callers keep minimum-work semantics: skip
        // ListClonesAsync entirely when a linked PR or a WorkingBranchId is
        // enough. Hot-path callers (graph build) go through the context
        // overload with a pre-built clones list.
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            var emptyContext = new BranchResolutionContext(
                Array.Empty<Homespun.Shared.Models.Git.CloneInfo>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
            return await ResolveIssueBranchAsync(projectId, issueId, emptyContext);
        }

        var prBranches = dataStore.GetPullRequestsByProject(projectId)
            .Where(pr => !string.IsNullOrEmpty(pr.BeadsIssueId) && !string.IsNullOrEmpty(pr.BranchName))
            .GroupBy(pr => pr.BeadsIssueId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().BranchName!, StringComparer.Ordinal);

        // Short-circuit: linked PR hit — no need to scan clones.
        if (prBranches.ContainsKey(issueId))
        {
            return await ResolveIssueBranchAsync(
                projectId, issueId,
                new BranchResolutionContext(
                    Array.Empty<Homespun.Shared.Models.Git.CloneInfo>(),
                    prBranches));
        }

        if (string.IsNullOrEmpty(project.LocalPath))
        {
            return await ResolveIssueBranchAsync(
                projectId, issueId,
                new BranchResolutionContext(
                    Array.Empty<Homespun.Shared.Models.Git.CloneInfo>(),
                    prBranches));
        }

        // Short-circuit: issue has an explicit WorkingBranchId — no clones
        // scan needed either. The context overload reads GetIssueAsync before
        // falling through to the clones list, so a stub with empty clones
        // still resolves correctly.
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue != null && !string.IsNullOrWhiteSpace(issue.WorkingBranchId))
        {
            return await ResolveIssueBranchAsync(
                projectId, issueId,
                new BranchResolutionContext(
                    Array.Empty<Homespun.Shared.Models.Git.CloneInfo>(),
                    prBranches));
        }

        var clones = await gitCloneService.ListClonesAsync(project.LocalPath);
        return await ResolveIssueBranchAsync(
            projectId, issueId,
            new BranchResolutionContext(clones, prBranches));
    }

    /// <inheritdoc />
    public async Task<string?> ResolveIssueBranchAsync(
        string projectId,
        string issueId,
        BranchResolutionContext context)
    {
        using var activity = OpenSpecActivitySource.Instance.StartActivity("openspec.branch.resolve");
        activity?.SetTag("project.id", projectId);
        activity?.SetTag("issue.id", issueId);

        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when resolving branch for issue {IssueId}",
                projectId, issueId);
            activity?.SetTag("branch.source", "none");
            return null;
        }

        // 1. Linked PR lookup — dictionary, not a data-store fan-out.
        if (context.PrBranchByIssueId.TryGetValue(issueId, out var prBranch)
            && !string.IsNullOrEmpty(prBranch))
        {
            logger.LogDebug("Found linked PR branch {BranchName} for issue {IssueId}", prBranch, issueId);
            activity?.SetTag("branch.source", "linked-pr");
            return prBranch;
        }

        if (string.IsNullOrEmpty(project.LocalPath))
        {
            logger.LogWarning("Project {ProjectId} has no local path configured", projectId);
            activity?.SetTag("branch.source", "none");
            return null;
        }

        // 2. Issue's WorkingBranchId — still a single fleece lookup per node.
        var issue = await fleeceService.GetIssueAsync(project.LocalPath, issueId);
        if (issue != null && !string.IsNullOrWhiteSpace(issue.WorkingBranchId))
        {
            var branchName = BranchNameGenerator.GenerateBranchNamePreview(
                issueId, issue.Type, issue.Title, issue.WorkingBranchId);
            logger.LogDebug("Resolved branch {BranchName} from WorkingBranchId for issue {IssueId}",
                branchName, issueId);
            activity?.SetTag("branch.source", "working-branch");
            return branchName;
        }

        // 3. Existing clones — reuse the hoisted list instead of re-scanning disk.
        foreach (var clone in context.Clones)
        {
            if (string.IsNullOrEmpty(clone.Branch))
            {
                continue;
            }

            var cloneBranchName = clone.Branch.Replace("refs/heads/", "");
            var extractedIssueId = BranchNameParser.ExtractIssueId(cloneBranchName);

            if (extractedIssueId == issueId)
            {
                logger.LogDebug("Found existing clone with branch {BranchName} for issue {IssueId}",
                    cloneBranchName, issueId);
                activity?.SetTag("branch.source", "clone");
                return cloneBranchName;
            }
        }

        logger.LogDebug("No linked PR, working branch ID, or existing clone found for issue {IssueId}", issueId);
        activity?.SetTag("branch.source", "none");
        return null;
    }
}
