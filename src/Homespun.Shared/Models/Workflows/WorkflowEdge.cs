namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Type of edge in the workflow.
/// </summary>
public enum WorkflowEdgeType
{
    /// <summary>Default edge - always followed when source completes successfully.</summary>
    Default,

    /// <summary>Conditional edge - followed only when condition evaluates to true.</summary>
    Conditional,

    /// <summary>Error edge - followed when source node fails.</summary>
    Error
}

/// <summary>
/// Represents an edge (connection) between two nodes in the workflow.
/// </summary>
public class WorkflowEdge
{
    /// <summary>
    /// Unique identifier for this edge.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The source node ID.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// The target node ID.
    /// </summary>
    public required string Target { get; set; }

    /// <summary>
    /// The source handle ID (for nodes with multiple outputs).
    /// </summary>
    public string? SourceHandle { get; set; }

    /// <summary>
    /// The target handle ID (for nodes with multiple inputs).
    /// </summary>
    public string? TargetHandle { get; set; }

    /// <summary>
    /// The type of edge.
    /// </summary>
    public WorkflowEdgeType Type { get; set; } = WorkflowEdgeType.Default;

    /// <summary>
    /// Optional label for the edge.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Condition configuration for conditional edges.
    /// </summary>
    public EdgeCondition? Condition { get; set; }

    /// <summary>
    /// Whether this edge is animated in the visual editor.
    /// </summary>
    public bool Animated { get; set; } = false;
}

/// <summary>
/// Condition configuration for conditional edges.
/// </summary>
public class EdgeCondition
{
    /// <summary>
    /// The type of condition to evaluate.
    /// </summary>
    public EdgeConditionType Type { get; set; }

    /// <summary>
    /// The field/variable to evaluate from the source node output.
    /// Uses dot notation for nested fields (e.g., "result.status").
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public ComparisonOperator Operator { get; set; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Expression for complex conditions (when Type is Expression).
    /// </summary>
    public string? Expression { get; set; }
}

/// <summary>
/// Type of edge condition.
/// </summary>
public enum EdgeConditionType
{
    /// <summary>Simple field comparison.</summary>
    FieldComparison,

    /// <summary>Check if output contains a value.</summary>
    Contains,

    /// <summary>Custom expression evaluation.</summary>
    Expression,

    /// <summary>Always true (for explicit default paths).</summary>
    Always
}

/// <summary>
/// Comparison operators for edge conditions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Equal to.</summary>
    Equals,

    /// <summary>Not equal to.</summary>
    NotEquals,

    /// <summary>Greater than.</summary>
    GreaterThan,

    /// <summary>Less than.</summary>
    LessThan,

    /// <summary>Greater than or equal to.</summary>
    GreaterThanOrEquals,

    /// <summary>Less than or equal to.</summary>
    LessThanOrEquals,

    /// <summary>Contains substring.</summary>
    Contains,

    /// <summary>Starts with.</summary>
    StartsWith,

    /// <summary>Ends with.</summary>
    EndsWith,

    /// <summary>Matches regex pattern.</summary>
    Matches,

    /// <summary>Is null or empty.</summary>
    IsEmpty,

    /// <summary>Is not null or empty.</summary>
    IsNotEmpty
}
