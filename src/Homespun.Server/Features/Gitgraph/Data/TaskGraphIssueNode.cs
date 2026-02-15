using Fleece.Core.Models;

namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Adapts a Fleece TaskGraphNode to the IGraphNode interface for task graph visualization.
/// Used by the new task graph rendering mode where actionable items appear at lane 0 (left)
/// and parent/blocking issues appear at higher lanes (right).
/// </summary>
public class TaskGraphIssueNode : IGraphNode
{
    private readonly TaskGraphNode _taskGraphNode;
    private readonly Issue _issue;
    private readonly IReadOnlyList<string> _parentIds;
    private readonly PullRequestStatus? _prStatus;

    public TaskGraphIssueNode(
        TaskGraphNode taskGraphNode,
        Issue issue,
        IReadOnlyList<string> parentIds,
        PullRequestStatus? prStatus = null)
    {
        _taskGraphNode = taskGraphNode;
        _issue = issue;
        _parentIds = parentIds;
        _prStatus = prStatus;
    }

    public string Id => $"issue-{_issue.Id}";

    public string Title => _issue.Title;

    public GraphNodeType NodeType => GraphNodeType.Issue;

    public GraphNodeStatus Status => _issue.Status switch
    {
        IssueStatus.Closed => GraphNodeStatus.Completed,
        IssueStatus.Complete => GraphNodeStatus.Completed,
        IssueStatus.Archived => GraphNodeStatus.Abandoned,
        IssueStatus.Deleted => GraphNodeStatus.Abandoned,
        IssueStatus.Open => GraphNodeStatus.Open,
        IssueStatus.Progress => GraphNodeStatus.Open,
        IssueStatus.Review => GraphNodeStatus.Open,
        _ => GraphNodeStatus.Open
    };

    public IReadOnlyList<string> ParentIds => _parentIds;

    public string BranchName => $"issue-{_issue.Id}";

    public DateTime SortDate => _issue.Status is IssueStatus.Closed or IssueStatus.Complete
        ? _issue.StatusLastUpdate.DateTime
        : _issue.CreatedAt.DateTime;

    /// <summary>
    /// Time dimension based on the task graph lane.
    /// Lane 0 (actionable) = time dimension 2 (next up)
    /// Higher lanes = higher time dimensions (blocked/future work)
    /// </summary>
    public int TimeDimension => 2 + _taskGraphNode.Lane;

    public string? Url => null;

    public string? Color => _prStatus.HasValue ? GetPrStatusColor(_prStatus.Value) : GetIssueColor(_issue.Type, _issue.Status);

    public string? Tag => _prStatus.HasValue ? GetPrStatusTag(_prStatus.Value) : _issue.Type.ToString();

    /// <summary>
    /// The PR status for this issue, if a linked PR exists.
    /// </summary>
    public PullRequestStatus? LinkedPrStatus => _prStatus;

    public int? PullRequestNumber => null;

    public string? IssueId => _issue.Id;

    /// <summary>
    /// Whether the issue has a non-empty description.
    /// </summary>
    public bool? HasDescription => !string.IsNullOrWhiteSpace(_issue.Description);

    /// <summary>
    /// Original Fleece Issue for access to additional properties.
    /// </summary>
    public Issue Issue => _issue;

    /// <summary>
    /// The task graph node containing lane and row information.
    /// </summary>
    public TaskGraphNode TaskGraphNode => _taskGraphNode;

    /// <summary>
    /// Whether this issue is actionable (lane 0 = no blocking issues).
    /// </summary>
    public bool IsActionable => _taskGraphNode.IsActionable;

    // IGraphNode.IsActionable implementation
    bool? IGraphNode.IsActionable => IsActionable;

    /// <summary>
    /// The lane assignment from the task graph (0 = actionable/left, higher = blocked/right).
    /// </summary>
    public int Lane => _taskGraphNode.Lane;

    // IGraphNode.TaskGraphLane implementation
    int? IGraphNode.TaskGraphLane => Lane;

    /// <summary>
    /// The row position in the task graph for vertical ordering.
    /// </summary>
    public int Row => _taskGraphNode.Row;

    // IGraphNode.TaskGraphRow implementation
    int? IGraphNode.TaskGraphRow => Row;

    /// <summary>
    /// Priority level for sorting within the same lane.
    /// </summary>
    public int? Priority => _issue.Priority;

    /// <summary>
    /// Gets the base color for an issue based on its type.
    /// Bug type shows as red, all other types show as blue.
    /// </summary>
    private static string GetIssueColor(IssueType type, IssueStatus status)
    {
        // Bug type always shows as red
        if (type == IssueType.Bug) return "#ef4444"; // Red

        // All other types (Task, Feature, Chore) show as blue
        return "#3b82f6"; // Blue
    }

    private static string GetPrStatusColor(PullRequestStatus status) => status switch
    {
        PullRequestStatus.ChecksFailing => "#ef4444",   // Red - attention needed
        PullRequestStatus.Conflict => "#f97316",        // Orange - conflicts
        PullRequestStatus.InProgress => "#3b82f6",      // Blue - work in progress
        PullRequestStatus.ReadyForReview => "#eab308",  // Yellow - awaiting review
        PullRequestStatus.ReadyForMerging => "#22c55e", // Green - ready to merge
        PullRequestStatus.Merged => "#9ca3af",          // Light gray (Tailwind gray-400) - merged
        PullRequestStatus.Closed => "#6b7280",          // Gray - closed
        _ => "#6b7280"                                  // Gray
    };

    private static string GetPrStatusTag(PullRequestStatus status) => status switch
    {
        PullRequestStatus.ChecksFailing => "Checks Failing",
        PullRequestStatus.Conflict => "Conflicts",
        PullRequestStatus.InProgress => "In Progress",
        PullRequestStatus.ReadyForReview => "Ready for Review",
        PullRequestStatus.ReadyForMerging => "Ready to Merge",
        PullRequestStatus.Merged => "Merged",
        PullRequestStatus.Closed => "Closed",
        _ => "PR"
    };
}
