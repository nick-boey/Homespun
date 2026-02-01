using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IGraphService that builds graph data from MockDataStore.
/// </summary>
public class MockGraphService : IGraphService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockGraphService> _logger;
    private readonly GraphBuilder _graphBuilder = new();
    private readonly GitgraphApiMapper _mapper = new();

    public MockGraphService(
        IDataStore dataStore,
        ILogger<MockGraphService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraph for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project not found: {ProjectId}", projectId);
            return Task.FromResult(new Graph([], new Dictionary<string, GraphBranch>()));
        }

        // Convert stored PullRequests to PullRequestInfo
        var pullRequests = _dataStore.GetPullRequestsByProject(projectId);
        var prInfos = pullRequests.Select(ConvertToPullRequestInfo).ToList();

        _logger.LogDebug("[Mock] Building graph with {PrCount} PRs", prInfos.Count);

        // Use the existing GraphBuilder to construct the graph
        var graph = _graphBuilder.Build(prInfos, [], maxPastPRs);
        return Task.FromResult(graph);
    }

    public async Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5, bool useCache = true)
    {
        _logger.LogDebug("[Mock] BuildGraphJson for project {ProjectId} (useCache: {UseCache})", projectId, useCache);

        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        return _mapper.ToJson(graph);
    }

    public async Task<GitgraphJsonData> BuildGraphJsonWithFreshDataAsync(string projectId, int? maxPastPRs = 5)
    {
        _logger.LogDebug("[Mock] BuildGraphJsonWithFreshData for project {ProjectId}", projectId);

        // In mock mode, just build the graph normally
        var graph = await BuildGraphAsync(projectId, maxPastPRs);
        return _mapper.ToJson(graph);
    }

    public DateTime? GetCacheTimestamp(string projectId)
    {
        // Mock mode doesn't have real caching, return current time
        return DateTime.UtcNow;
    }

    public bool HasCachedData(string projectId)
    {
        // Mock mode always has "cached" data available
        return true;
    }

    private static PullRequestInfo ConvertToPullRequestInfo(PullRequest pr)
    {
        return new PullRequestInfo
        {
            Number = pr.GitHubPRNumber ?? 0,
            Title = pr.Title ?? "Untitled",
            Body = pr.Description,
            Status = ConvertStatus(pr.Status),
            BranchName = pr.BranchName,
            HtmlUrl = pr.GitHubPRNumber != null
                ? $"https://github.com/mock-org/mock-repo/pull/{pr.GitHubPRNumber}"
                : null,
            CreatedAt = pr.CreatedAt,
            UpdatedAt = pr.UpdatedAt,
            MergedAt = null, // Open PRs are not merged
            ChecksPassing = true,
            IsApproved = pr.Status == OpenPullRequestStatus.Approved
        };
    }

    private static PullRequestStatus ConvertStatus(OpenPullRequestStatus status)
    {
        return status switch
        {
            OpenPullRequestStatus.InDevelopment => PullRequestStatus.InProgress,
            OpenPullRequestStatus.ReadyForReview => PullRequestStatus.ReadyForReview,
            OpenPullRequestStatus.HasReviewComments => PullRequestStatus.InProgress,
            OpenPullRequestStatus.Approved => PullRequestStatus.ReadyForMerging,
            _ => PullRequestStatus.InProgress
        };
    }
}
