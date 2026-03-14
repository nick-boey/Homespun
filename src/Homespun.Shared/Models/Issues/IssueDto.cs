using Fleece.Core.Models;

namespace Homespun.Shared.Models.Issues;

/// <summary>
/// DTO for issue data in apply agent changes operations.
/// </summary>
public class IssueDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public IssueStatus Status { get; set; }
    public IssueType Type { get; set; }
    public int? Priority { get; set; }
    public List<int> LinkedPRs { get; set; } = [];
    public List<string> LinkedIssues { get; set; } = [];
    public List<ParentIssueRef> ParentIssues { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? WorkingBranchId { get; set; }
    public ExecutionMode ExecutionMode { get; set; }
    public string? CreatedBy { get; set; }
    public string? AssignedTo { get; set; }
    public DateTimeOffset LastUpdate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Extension methods for converting between Issue and IssueDto.
/// </summary>
public static class IssueDtoExtensions
{
    public static IssueDto ToDto(this Issue issue)
    {
        return new IssueDto
        {
            Id = issue.Id,
            Title = issue.Title,
            Description = issue.Description,
            Status = issue.Status,
            Type = issue.Type,
            Priority = issue.Priority,
            LinkedPRs = issue.LinkedPRs.ToList(),
            LinkedIssues = issue.LinkedIssues.ToList(),
            ParentIssues = issue.ParentIssues.ToList(),
            Tags = issue.Tags.ToList(),
            WorkingBranchId = issue.WorkingBranchId,
            ExecutionMode = issue.ExecutionMode,
            CreatedBy = issue.CreatedBy,
            AssignedTo = issue.AssignedTo,
            LastUpdate = issue.LastUpdate,
            CreatedAt = issue.CreatedAt
        };
    }
}