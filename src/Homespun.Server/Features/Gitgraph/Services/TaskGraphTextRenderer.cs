using System.Text;
using Fleece.Core.Models;
using Fleece.Core.Models.Graph;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Renders a Fleece.Core v3 GraphLayout&lt;Issue&gt; as a plain text string with box-drawing connectors.
/// Uses the layout's Nodes list (row-ordered) and Edge list as the source of truth for geometry;
/// no connector inference is performed here.
/// </summary>
public static class TaskGraphTextRenderer
{
    // Node markers
    private const char ActionableMarker = '○';  // ○ - actionable (next)
    private const char OpenMarker = '◌';         // ◌ - open but not actionable
    private const char CompleteMarker = '●';     // ● - complete
    private const char ClosedMarker = '⊘';       // ⊘ - closed/archived

    // Box-drawing characters
    private const char Horizontal = '─';  // ─
    private const char TopRight = '┐';    // ┐
    private const char RightTee = '┤';    // ┤
    private const char BottomRight = '└'; // └

    /// <summary>
    /// Renders the task graph layout as a text string.
    /// </summary>
    public static string Render(GraphLayout<Issue> layout, IReadOnlySet<string>? actionableIds = null)
    {
        if (layout.Nodes.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var node in layout.Nodes)
        {
            var lane = node.Lane;
            var isActionable = actionableIds?.Contains(node.Node.Id)
                ?? IsActionableByLayout(layout, node.Node.Id, node.Node.Status);
            var marker = GetMarker(node.Node, isActionable);

            // SeriesCornerToParent edges produce └─marker on the TARGET (parent) row
            var incomingCorner = layout.Edges.FirstOrDefault(e =>
                string.Equals(e.To.Id, node.Node.Id, StringComparison.OrdinalIgnoreCase)
                && e.Kind == EdgeKind.SeriesCornerToParent);

            // ParallelChildToSpine edges produce marker─┐/marker─┤ on the SOURCE (child) row
            var outgoingParallel = layout.Edges.FirstOrDefault(e =>
                string.Equals(e.From.Id, node.Node.Id, StringComparison.OrdinalIgnoreCase)
                && e.Kind == EdgeKind.ParallelChildToSpine);

            var nodeRow = new StringBuilder();

            if (incomingCorner != null)
            {
                // └─marker: series chain corner connecting the last child's lane to this parent's lane
                var fromLane = incomingCorner.Start.Lane;
                nodeRow.Append(new string(' ', fromLane * 2));
                nodeRow.Append(BottomRight);
                for (var col = fromLane * 2 + 1; col < lane * 2; col++)
                    nodeRow.Append(Horizontal);
                nodeRow.Append(marker);
            }
            else if (outgoingParallel != null)
            {
                // marker─┐ / marker─┤: parallel child connecting to parent spine
                var pivot = outgoingParallel.PivotLane ?? (lane + 1);
                nodeRow.Append(new string(' ', lane * 2));
                nodeRow.Append(marker);
                nodeRow.Append(Horizontal);
                for (var col = lane * 2 + 2; col < pivot * 2; col++)
                    nodeRow.Append(Horizontal);
                nodeRow.Append(IsFirstParallelChild(layout, outgoingParallel.To.Id, node.Row) ? TopRight : RightTee);
            }
            else
            {
                // Plain: orphan, root, or series sibling (SeriesSibling edges have no text connector)
                nodeRow.Append(new string(' ', lane * 2));
                nodeRow.Append(marker);
            }

            sb.Append(nodeRow);
            sb.Append("  ");
            sb.Append(node.Node.Id);
            sb.Append(' ');
            sb.Append(node.Node.Title);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static bool IsFirstParallelChild(GraphLayout<Issue> layout, string parentId, int currentRow)
    {
        return !layout.Edges.Any(e =>
            string.Equals(e.To.Id, parentId, StringComparison.OrdinalIgnoreCase)
            && e.Kind == EdgeKind.ParallelChildToSpine
            && e.Start.Row < currentRow);
    }

    private static bool IsActionableByLayout(GraphLayout<Issue> layout, string issueId, IssueStatus status)
    {
        if (status is IssueStatus.Progress or IssueStatus.Complete or IssueStatus.Closed or IssueStatus.Archived)
            return false;
        return !layout.Edges.Any(e => string.Equals(e.To.Id, issueId, StringComparison.OrdinalIgnoreCase));
    }

    private static char GetMarker(Issue issue, bool isActionable)
    {
        return issue.Status switch
        {
            IssueStatus.Complete => CompleteMarker,
            IssueStatus.Closed or IssueStatus.Archived => ClosedMarker,
            _ => isActionable ? ActionableMarker : OpenMarker
        };
    }
}
