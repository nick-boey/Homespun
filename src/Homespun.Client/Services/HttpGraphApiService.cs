using System.Net.Http.Json;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Gitgraph;

namespace Homespun.Client.Services;

public class HttpGraphApiService(HttpClient http)
{
    private const string BaseUrl = "api/graph";

    /// <summary>
    /// Gets the raw graph API response data from the server.
    /// This returns the full response including commit-level agent status data.
    /// Always uses cached data when available.
    /// </summary>
    public async Task<GraphApiResponse?> GetGraphDataAsync(string projectId, int? maxPastPRs = null)
    {
        var url = $"{BaseUrl}/{projectId}";
        if (maxPastPRs.HasValue)
            url += $"?maxPastPRs={maxPastPRs.Value}";
        return await http.GetFromJsonAsync<GraphApiResponse>(url);
    }

    /// <summary>
    /// Performs an incremental refresh: fetches only open PRs from GitHub,
    /// compares with cache to detect changes, and updates the cache.
    /// Falls back to full fetch if no cache exists.
    /// </summary>
    public async Task<GraphApiResponse?> RefreshGraphDataAsync(string projectId, int? maxPastPRs = null)
    {
        var url = $"{BaseUrl}/{projectId}/refresh";
        if (maxPastPRs.HasValue)
            url += $"?maxPastPRs={maxPastPRs.Value}";
        var response = await http.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GraphApiResponse>();
    }

    /// <summary>
    /// Gets graph data using ONLY cached data. No GitHub API calls are made.
    /// Returns null if no cache exists.
    /// </summary>
    public async Task<GraphApiResponse?> GetCachedGraphDataAsync(string projectId, int? maxPastPRs = null)
    {
        try
        {
            var url = $"{BaseUrl}/{projectId}/cached";
            if (maxPastPRs.HasValue)
                url += $"?maxPastPRs={maxPastPRs.Value}";
            var response = await http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GraphApiResponse>();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the task graph as plain text from the server.
    /// The task graph displays issues with actionable items on the left (lane 0)
    /// and parent/blocking issues on the right (higher lanes).
    /// </summary>
    public async Task<string?> GetTaskGraphTextAsync(string projectId)
    {
        try
        {
            var response = await http.GetAsync($"{BaseUrl}/{projectId}/taskgraph");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TaskGraphResponse?> GetTaskGraphDataAsync(string projectId, int maxPastPRs = 5)
    {
        try
        {
            return await http.GetFromJsonAsync<TaskGraphResponse>($"{BaseUrl}/{projectId}/taskgraph/data?maxPastPRs={maxPastPRs}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a GraphApiResponse to a Graph model for visualization.
    /// </summary>
    public static Graph ToGraph(GraphApiResponse data)
    {
        var nodes = data.Commits.Select(c => new GraphApiNode
        {
            Id = c.Hash,
            Title = c.Subject,
            NodeType = Enum.TryParse<GraphNodeType>(c.NodeType, true, out var nt) ? nt : GraphNodeType.Issue,
            Status = Enum.TryParse<GraphNodeStatus>(c.Status, true, out var ns) ? ns : GraphNodeStatus.Open,
            ParentIds = c.ParentIds,
            BranchName = c.Branch,
            SortDate = DateTime.MinValue,
            TimeDimension = c.TimeDimension,
            Url = c.Url,
            Color = c.Color,
            Tag = c.Tag,
            PullRequestNumber = c.PullRequestNumber,
            IssueId = c.IssueId,
            HasDescription = c.HasDescription
        }).ToList<IGraphNode>();

        var branches = data.Branches.ToDictionary(
            b => b.Name,
            b => new GraphBranch
            {
                Name = b.Name,
                Color = b.Color,
                ParentBranch = b.ParentBranch,
                ParentCommitId = b.ParentCommitId
            });

        return new Graph(
            nodes,
            branches,
            data.MainBranchName,
            data.HasMorePastPRs,
            data.TotalPastPRsShown);
    }

    /// <summary>
    /// Simple IGraphNode implementation for deserialized API data.
    /// </summary>
    private class GraphApiNode : IGraphNode
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public GraphNodeType NodeType { get; init; }
        public GraphNodeStatus Status { get; init; }
        public IReadOnlyList<string> ParentIds { get; init; } = [];
        public required string BranchName { get; init; }
        public DateTime SortDate { get; init; }
        public int TimeDimension { get; init; }
        public string? Url { get; init; }
        public string? Color { get; init; }
        public string? Tag { get; init; }
        public int? PullRequestNumber { get; init; }
        public string? IssueId { get; init; }
        public bool? HasDescription { get; init; }
    }
}
