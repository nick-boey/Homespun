namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// API response DTO for the graph endpoint. Matches the server's GitgraphJsonData format.
/// Used by Blazor WASM client to deserialize the graph API response.
/// </summary>
public class GraphApiResponse
{
    public string MainBranchName { get; set; } = "main";
    public List<GraphApiBranchData> Branches { get; set; } = [];
    public List<GraphApiCommitData> Commits { get; set; } = [];
    public bool HasMorePastPRs { get; set; }
    public int TotalPastPRsShown { get; set; }
}

/// <summary>
/// Branch data in the graph API response.
/// </summary>
public class GraphApiBranchData
{
    public required string Name { get; set; }
    public string? Color { get; set; }
    public string? ParentBranch { get; set; }
    public string? ParentCommitId { get; set; }
}

/// <summary>
/// Commit/node data in the graph API response.
/// </summary>
public class GraphApiCommitData
{
    public required string Hash { get; set; }
    public required string Subject { get; set; }
    public required string Branch { get; set; }
    public List<string> ParentIds { get; set; } = [];
    public string? Color { get; set; }
    public string? Tag { get; set; }
    public required string NodeType { get; set; }
    public required string Status { get; set; }
    public string? Url { get; set; }
    public int TimeDimension { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? IssueId { get; set; }
    public bool? HasDescription { get; set; }
    public AgentStatusData? AgentStatus { get; set; }
}
