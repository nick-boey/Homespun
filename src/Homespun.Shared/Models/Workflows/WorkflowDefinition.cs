namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Represents a complete workflow definition with nodes, edges, and settings.
/// </summary>
public class WorkflowDefinition
{
    /// <summary>
    /// Unique identifier for the workflow.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Project ID this workflow belongs to.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Display title for the workflow.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Optional description of what this workflow does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The nodes that make up this workflow.
    /// </summary>
    public List<WorkflowNode> Nodes { get; set; } = [];

    /// <summary>
    /// The edges connecting nodes in this workflow.
    /// </summary>
    public List<WorkflowEdge> Edges { get; set; } = [];

    /// <summary>
    /// Trigger configuration for this workflow.
    /// </summary>
    public WorkflowTrigger? Trigger { get; set; }

    /// <summary>
    /// Workflow-level settings.
    /// </summary>
    public WorkflowSettings Settings { get; set; } = new();

    /// <summary>
    /// Whether the workflow is currently enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Current version of the workflow definition.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// When the workflow was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the workflow was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who created the workflow.
    /// </summary>
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Workflow-level settings.
/// </summary>
public class WorkflowSettings
{
    /// <summary>
    /// Maximum number of concurrent node executions.
    /// </summary>
    public int MaxConcurrentNodes { get; set; } = 5;

    /// <summary>
    /// Default timeout in seconds for node execution.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 3600;

    /// <summary>
    /// Whether to continue execution on node failure.
    /// </summary>
    public bool ContinueOnFailure { get; set; } = false;

    /// <summary>
    /// Whether to retry failed nodes.
    /// </summary>
    public bool RetryOnFailure { get; set; } = false;

    /// <summary>
    /// Maximum number of retry attempts for failed nodes.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay in seconds between retry attempts.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 60;
}
