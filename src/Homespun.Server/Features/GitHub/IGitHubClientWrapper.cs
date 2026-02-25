using Octokit;

namespace Homespun.Features.GitHub;

/// <summary>
/// Wrapper interface for Octokit's GitHubClient to enable testing
/// </summary>
public interface IGitHubClientWrapper
{
    Task<Repository> GetRepositoryAsync(string owner, string repo);
    Task<IReadOnlyList<Octokit.PullRequest>> GetPullRequestsAsync(string owner, string repo, PullRequestRequest request);
    Task<Octokit.PullRequest> GetPullRequestAsync(string owner, string repo, int number);
    Task<Octokit.PullRequest> CreatePullRequestAsync(string owner, string repo, NewPullRequest newPullRequest);
    Task<IReadOnlyList<PullRequestReview>> GetPullRequestReviewsAsync(string owner, string repo, int number);
    Task<CombinedCommitStatus> GetCombinedCommitStatusAsync(string owner, string repo, string reference);
    Task<PullRequestMerge> MergePullRequestAsync(string owner, string repo, int number, MergePullRequest merge);
    Task<IReadOnlyList<PullRequestReviewComment>> GetPullRequestReviewCommentsAsync(string owner, string repo, int number);
    Task<CheckRunsResponse> GetCheckRunsForReferenceAsync(string owner, string repo, string reference);
}