namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Represents a section divider in the graph visualization (e.g., "CURRENT PRs" or "ISSUES").
/// </summary>
public class SectionDividerNode : IGraphNode
{
    private readonly string _label;
    private readonly int _timeDimension;

    public SectionDividerNode(string label, int timeDimension)
    {
        _label = label;
        _timeDimension = timeDimension;
    }

    public string Id => $"divider-{_label.ToLowerInvariant().Replace(" ", "-")}";

    public string Title => _label;

    public GraphNodeType NodeType => GraphNodeType.SectionDivider;

    public GraphNodeStatus Status => GraphNodeStatus.Completed;

    public IReadOnlyList<string> ParentIds => [];

    public string BranchName => "main";

    public DateTime SortDate => DateTime.MinValue;

    public int TimeDimension => _timeDimension;

    public string? Url => null;

    public string? Color => "#6b7280"; // Gray

    public string? Tag => null;

    public int? PullRequestNumber => null;

    public string? IssueId => null;
}
