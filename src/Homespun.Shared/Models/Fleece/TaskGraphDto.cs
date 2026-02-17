namespace Homespun.Shared.Models.Fleece;

public class TaskGraphResponse
{
    public List<TaskGraphNodeResponse> Nodes { get; set; } = [];
    public int TotalLanes { get; set; }
}

public class TaskGraphNodeResponse
{
    public IssueResponse Issue { get; set; } = new();
    public int Lane { get; set; }
    public int Row { get; set; }
    public bool IsActionable { get; set; }
}
