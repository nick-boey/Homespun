using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;
using Octokit;

namespace Homespun.Features.PullRequests;

/// <summary>
/// Service for managing PR workflow including status tracking, time calculation, and rebasing.
/// </summary>
public class PullRequestWorkflowService(
    IDataStore dataStore,
    ICommandRunner commandRunner,
    IConfiguration configuration,
    IGitHubClientWrapper githubClient,
    ILogger<PullRequestWorkflowService> logger)
{
    private string? GetGitHubToken()
    {
        // Priority: 1. User secrets (GitHub:Token), 2. Config/env var (GITHUB_TOKEN), 3. Direct env var
        return configuration["GitHub:Token"]
            ?? configuration["GITHUB_TOKEN"]
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    private void ConfigureClient()
    {
        var token = GetGitHubToken();
        if (githubClient is GitHubClientWrapper wrapper)
        {
            wrapper.SetToken(token);
        }
    }

    #region 2.1 Past PR Synchronization

    /// <summary>
    /// Gets all merged PRs with their calculated time values.
    /// Time is calculated based on merge order: most recent = 0, older = negative.
    /// </summary>
    public async Task<List<PullRequestWithTime>> GetMergedPullRequestsWithTimeAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogDebug("Cannot get merged PRs: project {ProjectId} not found or missing GitHub config", projectId);
            return [];
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching merged PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            var request = new PullRequestRequest { State = ItemStateFilter.Closed };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            // Filter to only merged PRs and map to PullRequestInfo
            var mergedPRs = prs
                .Where(pr => pr.Merged)
                .Select(MapToPullRequestInfo)
                .ToList();

            logger.LogInformation("Found {Count} merged PRs from {Owner}/{Repo}", mergedPRs.Count, project.GitHubOwner, project.GitHubRepo);

            // Calculate time values
            var times = PullRequestTimeCalculator.CalculateTimesForMergedPRs(mergedPRs);

            // Return ordered by merge time (most recent first)
            return mergedPRs
                .OrderByDescending(pr => pr.MergedAt)
                .Select(pr => new PullRequestWithTime(pr, times.GetValueOrDefault(pr.Number, int.MinValue)))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch merged PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    /// <summary>
    /// Gets all closed (not merged) PRs with their calculated time values.
    /// </summary>
    public async Task<List<PullRequestWithTime>> GetClosedPullRequestsWithTimeAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogDebug("Cannot get closed PRs: project {ProjectId} not found or missing GitHub config", projectId);
            return [];
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching closed PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            var request = new PullRequestRequest { State = ItemStateFilter.Closed };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            // Separate merged and closed PRs
            var allPRs = prs.Select(MapToPullRequestInfo).ToList();
            var mergedPRs = allPRs.Where(pr => pr.Status == PullRequestStatus.Merged).ToList();
            var closedPRs = allPRs.Where(pr => pr.Status == PullRequestStatus.Closed).ToList();

            logger.LogInformation("Found {Count} closed (not merged) PRs from {Owner}/{Repo}", closedPRs.Count, project.GitHubOwner, project.GitHubRepo);

            // Calculate time for closed PRs relative to merged PRs
            var result = new List<PullRequestWithTime>();
            foreach (var closedPR in closedPRs.OrderByDescending(pr => pr.ClosedAt))
            {
                var time = PullRequestTimeCalculator.CalculateTimeForClosedPR(closedPR, mergedPRs);
                result.Add(new PullRequestWithTime(closedPR, time));
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch closed PRs from {Owner}/{Repo}", project.GitHubOwner, project.GitHubRepo);
            return [];
        }
    }

    #endregion

    #region 2.2 Current PR Status Tracking

    /// <summary>
    /// Gets all open PRs with their calculated status based on review state and CI checks.
    /// All open PRs have time = 1.
    /// </summary>
    public async Task<List<PullRequestWithStatus>> GetOpenPullRequestsWithStatusAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest { State = ItemStateFilter.Open };
            var prs = await githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);

            var result = new List<PullRequestWithStatus>();

            foreach (var pr in prs)
            {
                var prInfo = MapToPullRequestInfo(pr);
                var status = await DetermineStatusAndPopulatePrInfoAsync(project.GitHubOwner, project.GitHubRepo, pr, prInfo);
                result.Add(new PullRequestWithStatus(prInfo, status, 1)); // All open PRs have t=1
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Determines the status of a PR based on its state, reviews, and CI checks.
    /// Also populates the ChecksPassing, IsApproved, ApprovalCount, and ChangesRequestedCount
    /// properties on the PullRequestInfo object.
    /// </summary>
    private async Task<PullRequestStatus> DetermineStatusAndPopulatePrInfoAsync(
        string owner,
        string repo,
        Octokit.PullRequest pr,
        PullRequestInfo prInfo)
    {
        // Draft PRs are always InProgress
        if (pr.Draft)
        {
            return PullRequestStatus.InProgress;
        }

        // Get CI check status
        CombinedCommitStatus? commitStatus = null;
        try
        {
            commitStatus = await githubClient.GetCombinedCommitStatusAsync(owner, repo, pr.Head.Sha);
        }
        catch
        {
            // Ignore errors getting commit status
        }

        // Populate ChecksPassing based on commit status
        if (commitStatus?.State == CommitState.Success)
        {
            prInfo.ChecksPassing = true;
        }
        else if (commitStatus?.State == CommitState.Failure || commitStatus?.State == CommitState.Error)
        {
            prInfo.ChecksPassing = false;
        }
        // If Pending or null, leave ChecksPassing as null (checks are running)

        // Check if CI is failing
        if (commitStatus?.State == CommitState.Failure || commitStatus?.State == CommitState.Error)
        {
            return PullRequestStatus.ChecksFailing;
        }

        // Get reviews
        IReadOnlyList<PullRequestReview>? reviews = null;
        try
        {
            reviews = await githubClient.GetPullRequestReviewsAsync(owner, repo, pr.Number);
        }
        catch
        {
            // Ignore errors getting reviews
        }

        // Check review state and populate approval/changes requested data
        if (reviews != null && reviews.Count > 0)
        {
            // Get the most recent review from each reviewer
            var latestReviews = reviews
                .GroupBy(r => r.User?.Id)
                .Select(g => g.OrderByDescending(r => r.SubmittedAt).First())
                .ToList();

            // Count approvals
            var approvals = latestReviews.Where(r => r.State.Value == PullRequestReviewState.Approved).ToList();
            prInfo.ApprovalCount = approvals.Count;
            prInfo.IsApproved = approvals.Count > 0;

            // Count changes requested
            var changesRequested = latestReviews.Where(r => r.State.Value == PullRequestReviewState.ChangesRequested).ToList();
            prInfo.ChangesRequestedCount = changesRequested.Count;

            // Check if any reviewer requested changes
            if (changesRequested.Count > 0)
            {
                return PullRequestStatus.InProgress;
            }

            // Check if approved
            if (approvals.Count > 0)
            {
                // Only ready for merging if CI passes
                if (commitStatus?.State == CommitState.Success)
                {
                    return PullRequestStatus.ReadyForMerging;
                }
            }
        }

        // If CI passes and no blocking reviews, ready for review
        if (commitStatus?.State == CommitState.Success)
        {
            return PullRequestStatus.ReadyForReview;
        }

        // Default to InProgress
        return PullRequestStatus.InProgress;
    }

    #endregion

    #region 2.3 Automatic Rebasing

    /// <summary>
    /// Rebases all open PR branches onto the latest main branch.
    /// </summary>
    public async Task<RebaseResult> RebaseAllOpenPRsAsync(string projectId)
    {
        var result = new RebaseResult();

        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            result.Errors.Add("Project not found");
            return result;
        }

        // Get all tracked pull requests that are open
        var pullRequests = dataStore.GetPullRequestsByProject(projectId)
            .Where(pr => pr.BranchName != null)
            .ToList();

        // Fetch latest from origin
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", project.LocalPath);
        if (!fetchResult.Success)
        {
            result.Errors.Add($"Failed to fetch from origin: {fetchResult.Error}");
            return result;
        }

        foreach (var pullRequest in pullRequests)
        {
            if (string.IsNullOrEmpty(pullRequest.BranchName))
                continue;

            var rebaseSuccess = await RebaseBranchAsync(project, pullRequest, result);
            if (rebaseSuccess)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
            }
        }

        return result;
    }

    private async Task<bool> RebaseBranchAsync(Project project, PullRequest pullRequest, RebaseResult result)
    {
        var workingDir = pullRequest.ClonePath ?? project.LocalPath;
        var baseBranch = project.DefaultBranch ?? "main";

        // Perform rebase
        var rebaseResult = await commandRunner.RunAsync(
            "git",
            $"rebase origin/{baseBranch}",
            workingDir);

        if (!rebaseResult.Success)
        {
            // Abort the failed rebase
            await commandRunner.RunAsync("git", "rebase --abort", workingDir);

            result.Conflicts.Add(new RebaseConflict(
                pullRequest.BranchName!,
                pullRequest.Id,
                rebaseResult.Error ?? "Rebase failed"));

            return false;
        }

        // Push the rebased branch with force-with-lease for safety
        var pushResult = await commandRunner.RunAsync(
            "git",
            $"push --force-with-lease origin {pullRequest.BranchName}",
            workingDir);

        if (!pushResult.Success)
        {
            result.Errors.Add($"Failed to push {pullRequest.BranchName}: {pushResult.Error}");
            return false;
        }

        return true;
    }

    #endregion

    #region Helpers

    private static PullRequestInfo MapToPullRequestInfo(Octokit.PullRequest pr)
    {
        PullRequestStatus status;

        if (pr.Merged)
        {
            status = PullRequestStatus.Merged;
        }
        else if (pr.State.Value == ItemState.Closed)
        {
            status = PullRequestStatus.Closed;
        }
        else
        {
            // Will be refined by DetermineStatusAsync for open PRs
            status = PullRequestStatus.InProgress;
        }

        return new PullRequestInfo
        {
            Number = pr.Number,
            Title = pr.Title,
            Body = pr.Body,
            Status = status,
            BranchName = pr.Head.Ref,
            HtmlUrl = pr.HtmlUrl,
            CreatedAt = pr.CreatedAt.UtcDateTime,
            MergedAt = pr.MergedAt?.UtcDateTime,
            ClosedAt = pr.ClosedAt?.UtcDateTime,
            UpdatedAt = pr.UpdatedAt.UtcDateTime
        };
    }

    #endregion

    #region Merged PR Details

    /// <summary>
    /// Gets details for a merged pull request including linked issue information.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="prNumber">The GitHub PR number.</param>
    /// <returns>The merged PR details, or null if not found.</returns>
    public async Task<MergedPullRequestDetails?> GetMergedPullRequestDetailsAsync(string projectId, int prNumber)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            logger.LogDebug("Cannot get merged PR details: project {ProjectId} not found or missing GitHub config", projectId);
            return null;
        }

        ConfigureClient();

        try
        {
            logger.LogInformation("Fetching PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            var pr = await githubClient.GetPullRequestAsync(project.GitHubOwner, project.GitHubRepo, prNumber);
            var prInfo = MapToPullRequestInfo(pr);

            // Extract linked issue ID from branch name
            var linkedIssueId = BranchNameParser.ExtractIssueId(prInfo.BranchName);

            return new MergedPullRequestDetails
            {
                PullRequest = prInfo,
                LinkedIssueId = linkedIssueId,
                LinkedIssue = null // Issue loading handled separately by the API endpoint
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PR #{PrNumber} from {Owner}/{Repo}", prNumber, project.GitHubOwner, project.GitHubRepo);
            return null;
        }
    }

    #endregion
}
