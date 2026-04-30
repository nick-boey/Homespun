using Fleece.Core.Models;
using Fleece.Core.Models.Graph;
using Homespun.Features.Gitgraph.Services;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece;

/// <summary>
/// Maps Fleece.Core Issue models to response DTOs safe for serialization to trimmed clients.
/// </summary>
public static class IssueDtoMapper
{
    public static IssueResponse ToResponse(this Issue issue)
    {
        return new IssueResponse
        {
            Id = issue.Id,
            Title = issue.Title,
            Description = issue.Description,
            Status = issue.Status,
            Type = issue.Type,
            Priority = issue.Priority,
            LinkedPRs = issue.LinkedPRs.ToList(),
            LinkedIssues = issue.LinkedIssues.ToList(),
            ParentIssues = issue.ParentIssues.Select(p => new ParentIssueRefResponse
            {
                ParentIssue = p.ParentIssue,
                SortOrder = p.SortOrder
            }).ToList(),
            Tags = issue.Tags.ToList(),
            WorkingBranchId = issue.WorkingBranchId,
            ExecutionMode = issue.ExecutionMode,
            CreatedBy = issue.CreatedBy,
            AssignedTo = issue.AssignedTo,
            LastUpdate = issue.LastUpdate,
            CreatedAt = issue.CreatedAt
        };
    }

    public static List<IssueResponse> ToResponseList(this IEnumerable<Issue> issues)
    {
        return issues.Select(i => i.ToResponse()).ToList();
    }

    public static TaskGraphResponse ToResponse(this GraphLayout<Issue> layout)
    {
        return new TaskGraphResponse
        {
            Nodes = layout.Nodes.Select(n => new TaskGraphNodeResponse
            {
                Issue = n.Node.ToResponse(),
                Lane = n.Lane,
                Row = n.Row,
                IsActionable = n.Lane == 0,
                AppearanceIndex = n.AppearanceIndex,
                TotalAppearances = n.TotalAppearances
            }).ToList(),
            TotalLanes = layout.TotalLanes,
            TotalRows = layout.TotalRows,
            Edges = layout.Edges.Select(GitgraphApiMapper.MapEdge).ToList()
        };
    }

}
