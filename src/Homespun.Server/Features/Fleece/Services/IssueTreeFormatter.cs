using System.Text;
using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Formats an issue's hierarchical context (ancestors and direct children) as a tree string.
/// </summary>
public static class IssueTreeFormatter
{
    private const int IndentSize = 2;
    private const string Prefix = "- ";

    /// <summary>
    /// Formats the issue tree showing all ancestors and direct children.
    /// </summary>
    /// <param name="issue">The current issue to format the tree for.</param>
    /// <param name="allIssues">All issues to search for ancestors and children. Can be null or empty.</param>
    /// <returns>A formatted tree string with each issue on its own line.</returns>
    public static string FormatIssueTree(Issue issue, IReadOnlyList<Issue>? allIssues)
    {
        var sb = new StringBuilder();

        // Handle null or empty issue list - just return the current issue
        if (allIssues == null || allIssues.Count == 0)
        {
            AppendIssueLine(sb, issue, 0);
            return sb.ToString().TrimEnd();
        }

        var issueMap = allIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        // Build ancestor chain (from root to current issue)
        var ancestorChain = BuildAncestorChain(issue, issueMap);

        // Render ancestors (from root down to current issue)
        var depth = 0;
        foreach (var ancestor in ancestorChain)
        {
            AppendIssueLine(sb, ancestor, depth);
            depth++;
        }

        // Append current issue
        AppendIssueLine(sb, issue, depth);

        // Find and render direct children
        var directChildren = GetDirectChildren(issue.Id, allIssues);
        foreach (var child in directChildren)
        {
            AppendIssueLine(sb, child, depth + 1);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the ancestor chain from root to parent (not including the current issue).
    /// Follows only the first parent when an issue has multiple parents.
    /// </summary>
    private static List<Issue> BuildAncestorChain(Issue issue, Dictionary<string, Issue> issueMap)
    {
        var ancestors = new List<Issue>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = issue;

        // Walk up the parent chain
        while (current.ParentIssues.Count > 0)
        {
            // Follow first parent only (handles DAG with multiple parents)
            var firstParentRef = current.ParentIssues[0];
            var parentId = firstParentRef.ParentIssue;

            // Prevent infinite loops from circular references
            if (visited.Contains(parentId))
            {
                break;
            }

            // Check if parent exists in our issue set
            if (!issueMap.TryGetValue(parentId, out var parent))
            {
                break;
            }

            visited.Add(parentId);
            ancestors.Add(parent);
            current = parent;
        }

        // Reverse to get root-first order
        ancestors.Reverse();
        return ancestors;
    }

    /// <summary>
    /// Gets direct children of the specified issue, sorted by sort order.
    /// </summary>
    private static List<Issue> GetDirectChildren(string issueId, IReadOnlyList<Issue>? allIssues)
    {
        if (allIssues == null || allIssues.Count == 0)
        {
            return [];
        }

        return allIssues
            .Where(i => i.ParentIssues.Any(p =>
                string.Equals(p.ParentIssue, issueId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i =>
            {
                var parentRef = i.ParentIssues.FirstOrDefault(p =>
                    string.Equals(p.ParentIssue, issueId, StringComparison.OrdinalIgnoreCase));
                return parentRef?.SortOrder ?? "0";
            })
            .ThenBy(i => i.Id) // Secondary sort by ID for stability
            .ToList();
    }

    /// <summary>
    /// Appends a formatted issue line to the StringBuilder.
    /// Format: "  - {id} [{type}] [{status}] {title}"
    /// </summary>
    private static void AppendIssueLine(StringBuilder sb, Issue issue, int depth)
    {
        // Add indentation
        sb.Append(new string(' ', depth * IndentSize));

        // Add prefix and issue info
        sb.Append(Prefix);
        sb.Append(issue.Id);
        sb.Append(" [");
        sb.Append(issue.Type.ToString().ToLowerInvariant());
        sb.Append("] [");
        sb.Append(issue.Status.ToString().ToLowerInvariant());
        sb.Append("] ");
        sb.AppendLine(issue.Title);
    }
}
