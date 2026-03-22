using Homespun.Shared.Models.PullRequests;

namespace Homespun.Shared.Models.Git;

public class EnrichedCloneInfo
{
    public required CloneInfo Clone { get; set; }
    public string? LinkedIssueId { get; set; }
    public EnrichedIssueInfo? LinkedIssue { get; set; }
    public EnrichedPrInfo? LinkedPr { get; set; }
    public bool IsDeletable { get; set; }
    public string? DeletionReason { get; set; }
    public bool IsIssuesAgentClone { get; set; }
}

public class EnrichedIssueInfo
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public string? Type { get; set; }
}

public class EnrichedPrInfo
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public required PullRequestStatus Status { get; set; }
    public string? HtmlUrl { get; set; }
}
