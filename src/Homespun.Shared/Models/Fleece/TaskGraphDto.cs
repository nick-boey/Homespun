using Homespun.Shared.Models.Gitgraph;

namespace Homespun.Shared.Models.Fleece;

public class TaskGraphResponse
{
    public List<TaskGraphNodeResponse> Nodes { get; set; } = [];
    public int TotalLanes { get; set; }

    /// <summary>
    /// Merged/closed PRs to display at the top of the task graph.
    /// </summary>
    public List<TaskGraphPrResponse> MergedPrs { get; set; } = [];

    /// <summary>
    /// Whether there are more past PRs available to load.
    /// </summary>
    public bool HasMorePastPrs { get; set; }

    /// <summary>
    /// Total number of past PRs currently shown.
    /// </summary>
    public int TotalPastPrsShown { get; set; }

    /// <summary>
    /// Agent status data keyed by issue ID.
    /// </summary>
    public Dictionary<string, AgentStatusData> AgentStatuses { get; set; } = new();

    /// <summary>
    /// Linked PR information keyed by issue ID.
    /// </summary>
    public Dictionary<string, TaskGraphLinkedPr> LinkedPrs { get; set; } = new();
}

public class TaskGraphNodeResponse
{
    public IssueResponse Issue { get; set; } = new();
    public int Lane { get; set; }
    public int Row { get; set; }
    public bool IsActionable { get; set; }
}

/// <summary>
/// Represents a merged/closed PR in the task graph.
/// </summary>
public class TaskGraphPrResponse
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public bool IsMerged { get; set; }
    public bool HasDescription { get; set; }
    public AgentStatusData? AgentStatus { get; set; }
}

/// <summary>
/// Represents a PR linked to an issue.
/// </summary>
public class TaskGraphLinkedPr
{
    public int Number { get; set; }
    public string? Url { get; set; }
    public string Status { get; set; } = "";
}
