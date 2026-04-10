using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.Git;

public interface ICloneEnrichmentService
{
    Task<List<EnrichedCloneInfo>> EnrichClonesAsync(string projectId, string projectLocalPath);
}

public class CloneEnrichmentService(
    IGitCloneService gitCloneService,
    IProjectFleeceService fleeceService,
    IGraphCacheService graphCacheService,
    ILogger<CloneEnrichmentService> logger) : ICloneEnrichmentService
{
    private const string IssuesAgentPrefix = "issues-agent-";
    private const string RefsHeadsPrefix = "refs/heads/";

    public async Task<List<EnrichedCloneInfo>> EnrichClonesAsync(string projectId, string projectLocalPath)
    {
        var clones = await gitCloneService.ListClonesAsync(projectLocalPath);
        var cachedPRData = graphCacheService.GetCachedPRData(projectId);

        var result = new List<EnrichedCloneInfo>();

        foreach (var clone in clones)
        {
            var enriched = await EnrichCloneAsync(clone, projectLocalPath, cachedPRData);
            result.Add(enriched);
        }

        return result;
    }

    private async Task<EnrichedCloneInfo> EnrichCloneAsync(
        CloneInfo clone,
        string projectLocalPath,
        CachedPRData? cachedPRData)
    {
        var branchName = NormalizeBranchName(clone.Branch);
        var issueId = BranchNameParser.ExtractIssueId(branchName);
        var isIssuesAgentClone = branchName?.StartsWith(IssuesAgentPrefix, StringComparison.OrdinalIgnoreCase) ?? false;

        Issue? issue = null;
        if (!string.IsNullOrEmpty(issueId))
        {
            try
            {
                issue = await fleeceService.GetIssueAsync(projectLocalPath, issueId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch issue {IssueId} for clone {ClonePath}", issueId, clone.Path);
            }
        }

        var linkedPr = FindMatchingPR(branchName, cachedPRData);

        var (isDeletable, deletionReason) = DetermineDeletability(issue, linkedPr, issueId);

        return new EnrichedCloneInfo
        {
            Clone = clone,
            LinkedIssueId = issueId,
            LinkedIssue = issue != null ? MapIssue(issue) : null,
            LinkedPr = linkedPr != null ? MapPR(linkedPr) : null,
            IsDeletable = isDeletable,
            DeletionReason = deletionReason,
            IsIssuesAgentClone = isIssuesAgentClone
        };
    }

    private static string? NormalizeBranchName(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
            return null;

        if (branch.StartsWith(RefsHeadsPrefix, StringComparison.OrdinalIgnoreCase))
            return branch[RefsHeadsPrefix.Length..];

        return branch;
    }

    private static PullRequestInfo? FindMatchingPR(string? branchName, CachedPRData? cachedPRData)
    {
        if (string.IsNullOrEmpty(branchName) || cachedPRData == null)
            return null;

        // Check open PRs first
        var pr = cachedPRData.OpenPrs.FirstOrDefault(p =>
            string.Equals(p.BranchName, branchName, StringComparison.OrdinalIgnoreCase));

        if (pr != null)
            return pr;

        // Check closed PRs
        return cachedPRData.ClosedPrs.FirstOrDefault(p =>
            string.Equals(p.BranchName, branchName, StringComparison.OrdinalIgnoreCase));
    }

    private static (bool IsDeletable, string? Reason) DetermineDeletability(
        Issue? issue,
        PullRequestInfo? pr,
        string? issueId)
    {
        // PR is merged or closed
        if (pr != null)
        {
            if (pr.Status == PullRequestStatus.Merged)
                return (true, "PR has been merged");

            if (pr.Status == PullRequestStatus.Closed)
                return (true, "PR has been closed");
        }

        // Issue has terminal status
        if (issue != null)
        {
            if (issue.Status == IssueStatus.Complete)
                return (true, "Issue is complete");

            if (issue.Status == IssueStatus.Archived)
                return (true, "Issue is archived");

            if (issue.Status == IssueStatus.Closed)
                return (true, "Issue is closed");
        }

        // Issue ID extracted but issue not found (deleted)
        if (!string.IsNullOrEmpty(issueId) && issue == null)
            return (true, "Linked issue not found (may have been deleted)");

        return (false, null);
    }

    private static EnrichedIssueInfo MapIssue(Issue issue)
    {
        return new EnrichedIssueInfo
        {
            Id = issue.Id,
            Title = issue.Title,
            Status = issue.Status.ToString(),
            Type = issue.Type.ToString()
        };
    }

    private static EnrichedPrInfo MapPR(PullRequestInfo pr)
    {
        return new EnrichedPrInfo
        {
            Number = pr.Number,
            Title = pr.Title,
            Status = pr.Status,
            HtmlUrl = pr.HtmlUrl
        };
    }
}
