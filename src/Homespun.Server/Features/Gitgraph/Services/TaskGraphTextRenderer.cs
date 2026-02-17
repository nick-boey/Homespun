using System.Text;
using Fleece.Core.Models;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Renders a Fleece.Core TaskGraph as a plain text string with box-drawing connectors.
/// Pure function with no external dependencies.
/// </summary>
public static class TaskGraphTextRenderer
{
    // Node markers
    private const char ActionableMarker = '\u25CB';  // ○ - actionable (next)
    private const char OpenMarker = '\u25CC';         // ◌ - open but not actionable
    private const char CompleteMarker = '\u25CF';     // ● - complete
    private const char ClosedMarker = '\u2298';       // ⊘ - closed/archived

    // Box-drawing characters
    private const char Horizontal = '\u2500';  // ─
    private const char Vertical = '\u2502';    // │
    private const char TopRight = '\u2510';    // ┐
    private const char RightTee = '\u2524';    // ┤

    /// <summary>
    /// Renders the task graph as a text string.
    /// </summary>
    public static string Render(TaskGraph taskGraph)
    {
        if (taskGraph.Nodes.Count == 0)
            return string.Empty;

        // Group nodes into disconnected components by walking parent relationships.
        // Each group is rendered separately with a blank line between groups.
        var groups = GroupNodes(taskGraph.Nodes);

        var sb = new StringBuilder();
        var firstGroup = true;

        foreach (var group in groups)
        {
            if (!firstGroup)
                sb.Append('\n');
            firstGroup = false;

            RenderGroup(sb, group, taskGraph.TotalLanes);
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Groups nodes into disconnected components. Nodes in the same dependency tree
    /// belong to the same group. Groups are ordered by their first node's row.
    /// </summary>
    private static List<List<TaskGraphNode>> GroupNodes(IReadOnlyList<TaskGraphNode> nodes)
    {
        // Build a lookup of issue ID to node
        var nodeById = new Dictionary<string, TaskGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            nodeById[node.Issue.Id] = node;

        // Build adjacency: parent <-> child (undirected for grouping)
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!adjacency.ContainsKey(node.Issue.Id))
                adjacency[node.Issue.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parentRef in node.Issue.ParentIssues)
            {
                if (!nodeById.ContainsKey(parentRef.ParentIssue)) continue;

                adjacency[node.Issue.Id].Add(parentRef.ParentIssue);

                if (!adjacency.ContainsKey(parentRef.ParentIssue))
                    adjacency[parentRef.ParentIssue] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adjacency[parentRef.ParentIssue].Add(node.Issue.Id);
            }
        }

        // BFS to find connected components
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<List<TaskGraphNode>>();

        foreach (var node in nodes)
        {
            if (visited.Contains(node.Issue.Id)) continue;

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(node.Issue.Id);
            visited.Add(node.Issue.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                if (adjacency.TryGetValue(current, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }
            }

            // Collect nodes in this component, ordered by Row
            var group = nodes
                .Where(n => component.Contains(n.Issue.Id))
                .OrderBy(n => n.Row)
                .ToList();
            groups.Add(group);
        }

        // Order groups by their first node's row
        groups.Sort((a, b) => a[0].Row.CompareTo(b[0].Row));

        return groups;
    }

    /// <summary>
    /// Renders a single group of connected nodes.
    /// </summary>
    private static void RenderGroup(StringBuilder sb, List<TaskGraphNode> group, int totalLanes)
    {
        if (group.Count == 0) return;

        // Find the minimum lane in this group to normalize lane positions
        var minLane = group.Min(n => n.Lane);

        // Build a lookup: for each parent issue ID, track which children connect to it
        // (to determine whether to use ┐ or ┤)
        var childCountByParent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in group)
        {
            foreach (var parentRef in node.Issue.ParentIssues)
            {
                if (!childCountByParent.ContainsKey(parentRef.ParentIssue))
                    childCountByParent[parentRef.ParentIssue] = 0;
                childCountByParent[parentRef.ParentIssue]++;
            }
        }

        // Track how many children have been rendered per parent (for ┐ vs ┤ decision)
        var childrenRendered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < group.Count; i++)
        {
            var node = group[i];
            var lane = node.Lane - minLane;
            var marker = GetMarker(node);

            // Find the parent with the highest lane (closest connector)
            TaskGraphNode? parentNode = null;
            foreach (var parentRef in node.Issue.ParentIssues)
            {
                var candidate = group.FirstOrDefault(n =>
                    string.Equals(n.Issue.Id, parentRef.ParentIssue, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && (parentNode == null || candidate.Lane > parentNode.Lane))
                    parentNode = candidate;
            }

            var parentLane = parentNode != null ? parentNode.Lane - minLane : -1;

            // Build the node row
            var nodeRow = new StringBuilder();

            if (parentNode != null && parentLane > lane)
            {
                // Node connects to parent on the right
                // Leading spaces up to node position
                nodeRow.Append(new string(' ', lane * 2));
                nodeRow.Append(marker);
                nodeRow.Append(Horizontal);

                // Draw horizontal line to parent lane
                for (var col = lane + 1; col < parentLane; col++)
                {
                    nodeRow.Append(Horizontal);
                    nodeRow.Append(Horizontal);
                }

                // Junction at parent lane
                if (!childrenRendered.ContainsKey(parentNode.Issue.Id))
                    childrenRendered[parentNode.Issue.Id] = 0;
                childrenRendered[parentNode.Issue.Id]++;

                var isFirstChild = childrenRendered[parentNode.Issue.Id] == 1;
                nodeRow.Append(isFirstChild ? TopRight : RightTee);
            }
            else
            {
                // Orphan or parent on same lane - just place the marker
                nodeRow.Append(new string(' ', lane * 2));
                nodeRow.Append(marker);
            }

            // Append issue label
            sb.Append(nodeRow);
            sb.Append("  ");
            sb.Append(node.Issue.Id);
            sb.Append(' ');
            sb.Append(node.Issue.Title);
            sb.Append('\n');

            // Render connector row between this node and the next in the same group
            if (i < group.Count - 1 && parentNode != null && parentLane > lane)
            {
                // Find if the next node also connects to this parent or a parent at parentLane
                var connectorRow = new StringBuilder();
                connectorRow.Append(new string(' ', parentLane * 2));
                connectorRow.Append(Vertical);
                sb.Append(connectorRow);
                sb.Append('\n');
            }
        }
    }

    /// <summary>
    /// Gets the appropriate marker character for a node based on its status and actionability.
    /// </summary>
    private static char GetMarker(TaskGraphNode node)
    {
        return node.Issue.Status switch
        {
            IssueStatus.Complete => CompleteMarker,
            IssueStatus.Closed or IssueStatus.Archived => ClosedMarker,
            _ => node.IsActionable ? ActionableMarker : OpenMarker
        };
    }
}
