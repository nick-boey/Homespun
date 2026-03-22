namespace Homespun.Shared.Models.Workflows.NodeConfigs;

/// <summary>
/// Type of data transformation.
/// </summary>
public enum TransformType
{
    /// <summary>Map/rename fields from input to output.</summary>
    Map,

    /// <summary>Filter items from an array.</summary>
    Filter,

    /// <summary>Aggregate/reduce array data.</summary>
    Aggregate,

    /// <summary>Split a value into multiple outputs.</summary>
    Split,

    /// <summary>Merge multiple inputs into one.</summary>
    Merge,

    /// <summary>Parse JSON string to object.</summary>
    ParseJson,

    /// <summary>Convert object to JSON string.</summary>
    ToJson,

    /// <summary>Apply a template to generate output.</summary>
    Template,

    /// <summary>Execute a custom JavaScript expression.</summary>
    Expression
}

/// <summary>
/// Configuration for a transform node that manipulates data between nodes.
/// </summary>
public class TransformNodeConfig
{
    /// <summary>
    /// The type of transformation to apply.
    /// </summary>
    public TransformType TransformType { get; set; }

    /// <summary>
    /// Input path to read from (supports dot notation).
    /// </summary>
    public string? InputPath { get; set; }

    /// <summary>
    /// Output variable name to store result in.
    /// </summary>
    public string OutputVariable { get; set; } = "result";

    /// <summary>
    /// Configuration for map transformation.
    /// </summary>
    public MapTransformConfig? Map { get; set; }

    /// <summary>
    /// Configuration for filter transformation.
    /// </summary>
    public FilterTransformConfig? Filter { get; set; }

    /// <summary>
    /// Configuration for aggregate transformation.
    /// </summary>
    public AggregateTransformConfig? Aggregate { get; set; }

    /// <summary>
    /// Configuration for split transformation.
    /// </summary>
    public SplitTransformConfig? Split { get; set; }

    /// <summary>
    /// Configuration for merge transformation.
    /// </summary>
    public MergeTransformConfig? Merge { get; set; }

    /// <summary>
    /// Configuration for template transformation.
    /// </summary>
    public TemplateTransformConfig? Template { get; set; }

    /// <summary>
    /// Configuration for expression transformation.
    /// </summary>
    public ExpressionTransformConfig? Expression { get; set; }
}

/// <summary>
/// Configuration for map transformations.
/// </summary>
public class MapTransformConfig
{
    /// <summary>
    /// Field mappings from source to destination.
    /// Key = destination field, Value = source path (supports interpolation).
    /// </summary>
    public Dictionary<string, string> Mappings { get; set; } = [];

    /// <summary>
    /// Whether to include unmapped fields from source.
    /// </summary>
    public bool IncludeUnmapped { get; set; } = false;

    /// <summary>
    /// Fields to exclude from the output.
    /// </summary>
    public List<string>? ExcludeFields { get; set; }
}

/// <summary>
/// Configuration for filter transformations.
/// </summary>
public class FilterTransformConfig
{
    /// <summary>
    /// Filter condition expression.
    /// For array items, use 'item' to reference current item.
    /// Example: "item.status == 'active'"
    /// </summary>
    public required string Condition { get; set; }

    /// <summary>
    /// Whether to keep items matching the condition (true) or remove them (false).
    /// </summary>
    public bool Keep { get; set; } = true;

    /// <summary>
    /// Maximum number of items to return.
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>
/// Configuration for aggregate transformations.
/// </summary>
public class AggregateTransformConfig
{
    /// <summary>
    /// Aggregation operation.
    /// </summary>
    public AggregateOperation Operation { get; set; }

    /// <summary>
    /// Field to aggregate (for sum, avg, min, max operations).
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// Group by field (for grouped aggregations).
    /// </summary>
    public string? GroupBy { get; set; }

    /// <summary>
    /// Initial value for custom reduce operations.
    /// </summary>
    public string? InitialValue { get; set; }

    /// <summary>
    /// Reduce expression for custom aggregations.
    /// Use 'acc' for accumulator and 'item' for current item.
    /// </summary>
    public string? ReduceExpression { get; set; }
}

/// <summary>
/// Aggregation operations.
/// </summary>
public enum AggregateOperation
{
    /// <summary>Count items.</summary>
    Count,

    /// <summary>Sum numeric values.</summary>
    Sum,

    /// <summary>Calculate average.</summary>
    Average,

    /// <summary>Find minimum value.</summary>
    Min,

    /// <summary>Find maximum value.</summary>
    Max,

    /// <summary>Get first item.</summary>
    First,

    /// <summary>Get last item.</summary>
    Last,

    /// <summary>Concatenate string values.</summary>
    Concat,

    /// <summary>Custom reduce operation.</summary>
    Reduce
}

/// <summary>
/// Configuration for split transformations.
/// </summary>
public class SplitTransformConfig
{
    /// <summary>
    /// Delimiter for string splitting.
    /// </summary>
    public string? Delimiter { get; set; }

    /// <summary>
    /// Regex pattern for splitting.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Whether to trim whitespace from results.
    /// </summary>
    public bool TrimWhitespace { get; set; } = true;

    /// <summary>
    /// Whether to remove empty entries.
    /// </summary>
    public bool RemoveEmpty { get; set; } = true;

    /// <summary>
    /// Maximum number of splits.
    /// </summary>
    public int? MaxSplits { get; set; }
}

/// <summary>
/// Configuration for merge transformations.
/// </summary>
public class MergeTransformConfig
{
    /// <summary>
    /// Paths to merge (from context).
    /// </summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>
    /// How to handle conflicts when merging objects.
    /// </summary>
    public MergeConflictStrategy ConflictStrategy { get; set; } = MergeConflictStrategy.OverwriteWithLater;

    /// <summary>
    /// Whether to deep merge nested objects.
    /// </summary>
    public bool DeepMerge { get; set; } = true;
}

/// <summary>
/// Strategy for handling merge conflicts.
/// </summary>
public enum MergeConflictStrategy
{
    /// <summary>Later values overwrite earlier ones.</summary>
    OverwriteWithLater,

    /// <summary>Keep earlier values.</summary>
    KeepEarlier,

    /// <summary>Create an array with both values.</summary>
    CreateArray,

    /// <summary>Fail on conflict.</summary>
    Fail
}

/// <summary>
/// Configuration for template transformations.
/// </summary>
public class TemplateTransformConfig
{
    /// <summary>
    /// Template string with interpolation placeholders.
    /// </summary>
    public required string Template { get; set; }

    /// <summary>
    /// Output format (string, json, etc.).
    /// </summary>
    public string OutputFormat { get; set; } = "string";
}

/// <summary>
/// Configuration for expression transformations.
/// </summary>
public class ExpressionTransformConfig
{
    /// <summary>
    /// JavaScript expression to evaluate.
    /// Has access to 'input' (input data) and 'context' (workflow context).
    /// </summary>
    public required string Expression { get; set; }
}
