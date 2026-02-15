using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Builds a Graph from Fleece.Core's TaskGraph for the new task graph visualization mode.
/// In this mode:
/// - Lane 0 (left side) contains actionable items (issues with no blocking dependencies)
/// - Higher lanes (right side) contain parent/blocking issues
/// - This behaves like a flattened Gantt chart with abstract time representation
/// </summary>
public class TaskGraphBuilder
{
    /// <summary>
    /// Converts a Fleece.Core TaskGraph to a Homespun Graph for visualization.
    /// </summary>
    /// <param name="taskGraph">The task graph from Fleece.Core's TaskGraphService.</param>
    /// <param name="issuePrStatuses">Optional dictionary mapping issue IDs to their linked PR statuses.</param>
    public Graph Build(
        TaskGraph taskGraph,
        IReadOnlyDictionary<string, PullRequestStatus>? issuePrStatuses = null)
    {
        var nodes = new List<IGraphNode>();
        var branches = new Dictionary<string, GraphBranch>();
        var prStatusLookup = issuePrStatuses ?? new Dictionary<string, PullRequestStatus>();

        // Add main branch (used for lane 0)
        branches["main"] = new GraphBranch
        {
            Name = "main",
            Color = "#6b7280"  // Gray
        };

        // Process TaskGraphNodes in row order (row determines vertical position)
        var orderedNodes = taskGraph.Nodes
            .OrderBy(n => n.Row)
            .ThenBy(n => n.Lane)
            .ToList();

        foreach (var taskNode in orderedNodes)
        {
            var issue = taskNode.Issue;

            // Build parent IDs from the issue's ParentIssues list
            // These are the issues that block this one
            var parentIds = issue.ParentIssues
                .Select(p => $"issue-{p.ParentIssue}")
                .ToList();

            // Check if this issue has a linked PR status
            prStatusLookup.TryGetValue(issue.Id, out var issuePrStatus);

            // Create the branch for this issue
            var branchName = $"issue-{issue.Id}";
            var branchColor = issuePrStatus != default
                ? GetPrStatusColor(issuePrStatus)
                : GetIssueColor(issue.Type);

            branches[branchName] = new GraphBranch
            {
                Name = branchName,
                Color = branchColor,
                ParentBranch = taskNode.Lane == 0 ? "main" : parentIds.FirstOrDefault()?.Replace("issue-", "issue-") ?? "main",
                ParentCommitId = parentIds.FirstOrDefault()
            };

            var node = new TaskGraphIssueNode(
                taskNode,
                issue,
                parentIds,
                prStatus: issuePrStatus != default ? issuePrStatus : null);

            nodes.Add(node);
        }

        return new Graph(nodes, branches, "main", hasMorePastPRs: false, totalPastPRsShown: 0);
    }

    private static string GetPrStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.InProgress => "#3b82f6",     // Blue
        PullRequestStatus.ReadyForReview => "#eab308", // Yellow
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green
        PullRequestStatus.ChecksFailing => "#ef4444",  // Red
        PullRequestStatus.Conflict => "#f97316",       // Orange
        _ => "#6b7280"                                 // Gray
    };

    private static string GetIssueColor(IssueType type)
    {
        // Bug type always shows as red
        if (type == IssueType.Bug) return "#ef4444"; // Red

        // All other types (Task, Feature, Chore) show as blue
        return "#3b82f6"; // Blue
    }
}
