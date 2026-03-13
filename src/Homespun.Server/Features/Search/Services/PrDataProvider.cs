using Homespun.Features.PullRequests;
using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.Search;

/// <summary>
/// Adapter that wraps PullRequestWorkflowService to provide PR data for search.
/// </summary>
public class PrDataProvider(PullRequestWorkflowService workflowService) : IPrDataProvider
{
    /// <inheritdoc />
    public Task<List<PullRequestWithStatus>> GetOpenPullRequestsWithStatusAsync(string projectId)
    {
        return workflowService.GetOpenPullRequestsWithStatusAsync(projectId);
    }

    /// <inheritdoc />
    public Task<List<PullRequestWithTime>> GetMergedPullRequestsWithTimeAsync(string projectId)
    {
        return workflowService.GetMergedPullRequestsWithTimeAsync(projectId);
    }
}
