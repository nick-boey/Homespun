using Fleece.Core.Models;

namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// API response model for Fleece issues.
/// Uses regular settable properties to avoid deserialization issues with
/// Fleece.Core.Models.Issue's required init-only properties in trimmed environments (Blazor WASM).
/// </summary>
public class IssueResponse
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public IssueStatus Status { get; set; }
    public IssueType Type { get; set; }
    public int? Priority { get; set; }
    public int? LinkedPR { get; set; }
    public List<string> LinkedIssues { get; set; } = [];
    public List<ParentIssueRefResponse> ParentIssues { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? WorkingBranchId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
    public string? CreatedBy { get; set; }
    public string? AssignedTo { get; set; }
    public DateTimeOffset LastUpdate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// API response model for parent issue references.
/// </summary>
public class ParentIssueRefResponse
{
    public string ParentIssue { get; set; } = "";
    public string? SortOrder { get; set; }
}
