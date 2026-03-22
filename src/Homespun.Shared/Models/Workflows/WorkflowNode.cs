using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Type of workflow node.
/// </summary>
public enum WorkflowNodeType
{
    /// <summary>Start node - entry point for workflow execution.</summary>
    Start,

    /// <summary>Agent node - executes a Claude agent task.</summary>
    Agent,

    /// <summary>Gate node - requires approval or condition to proceed.</summary>
    Gate,

    /// <summary>Action node - performs a specific action (e.g., create PR, merge).</summary>
    Action,

    /// <summary>Transform node - transforms data between nodes.</summary>
    Transform,

    /// <summary>End node - marks workflow completion.</summary>
    End
}

/// <summary>
/// Represents a node in the workflow graph.
/// </summary>
public class WorkflowNode
{
    /// <summary>
    /// Unique identifier for this node within the workflow.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display label for the node.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// The type of node.
    /// </summary>
    public WorkflowNodeType Type { get; set; }

    /// <summary>
    /// X position for visual editor (React Flow).
    /// </summary>
    public double PositionX { get; set; }

    /// <summary>
    /// Y position for visual editor (React Flow).
    /// </summary>
    public double PositionY { get; set; }

    /// <summary>
    /// Node-specific configuration as JSON.
    /// Deserialize to appropriate config type based on node Type.
    /// </summary>
    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }

    /// <summary>
    /// Optional description of what this node does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this node is disabled.
    /// </summary>
    public bool Disabled { get; set; } = false;

    /// <summary>
    /// Custom timeout in seconds for this node (overrides workflow default).
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>
/// Position data for a workflow node.
/// </summary>
public class NodePosition
{
    /// <summary>
    /// X coordinate.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y coordinate.
    /// </summary>
    public double Y { get; set; }
}
