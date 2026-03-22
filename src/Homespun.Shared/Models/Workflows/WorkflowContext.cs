using System.Text.Json;

namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Context storage for workflow execution, containing variables and state.
/// </summary>
public class WorkflowContext
{
    /// <summary>
    /// Initial input data provided when the workflow was triggered.
    /// </summary>
    public Dictionary<string, object> Input { get; set; } = [];

    /// <summary>
    /// Variables that can be set and read during workflow execution.
    /// </summary>
    public Dictionary<string, object> Variables { get; set; } = [];

    /// <summary>
    /// Output data from each completed node, keyed by node ID.
    /// </summary>
    public Dictionary<string, NodeOutput> NodeOutputs { get; set; } = [];

    /// <summary>
    /// Environment variables available to nodes.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>
    /// Secrets available to nodes (values should be encrypted at rest).
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = [];

    /// <summary>
    /// Gets a variable value by path (supports dot notation).
    /// </summary>
    /// <param name="path">Variable path (e.g., "nodes.agent1.result.status").</param>
    /// <returns>The value if found, null otherwise.</returns>
    public object? GetValue(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var parts = path.Split('.', 2);
        var root = parts[0];

        object? currentValue = root switch
        {
            "input" => Input,
            "variables" or "vars" => Variables,
            "nodes" => NodeOutputs,
            "env" => Environment,
            _ => Variables.GetValueOrDefault(root)
        };

        if (parts.Length == 1 || currentValue == null)
            return currentValue;

        return ResolveNestedPath(currentValue, parts[1]);
    }

    private static object? ResolveNestedPath(object current, string path)
    {
        foreach (var part in path.Split('.'))
        {
            current = current switch
            {
                Dictionary<string, object> dict => dict.GetValueOrDefault(part),
                Dictionary<string, string> strDict => strDict.GetValueOrDefault(part),
                Dictionary<string, NodeOutput> nodeDict => nodeDict.GetValueOrDefault(part),
                NodeOutput nodeOutput => part switch
                {
                    "data" => nodeOutput.Data,
                    "status" => nodeOutput.Status,
                    "error" => nodeOutput.Error,
                    _ => nodeOutput.Data?.GetValueOrDefault(part)
                },
                JsonElement jsonElement => GetJsonElementValue(jsonElement, part),
                _ => null
            } ?? (object?)null!;

            if (current == null)
                return null;
        }

        return current;
    }

    private static object? GetJsonElementValue(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property))
        {
            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property
            };
        }
        return null;
    }
}

/// <summary>
/// Output data from a completed node.
/// </summary>
public class NodeOutput
{
    /// <summary>
    /// The completion status of the node.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Output data from the node.
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Error information if the node failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// When the node completed.
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Template for interpolating context values into strings.
/// Supports syntax like "Hello {{input.name}}, your PR #{{nodes.agent1.data.prNumber}} is ready."
/// </summary>
public static class ContextInterpolation
{
    /// <summary>
    /// Pattern for matching template variables: {{path.to.variable}}
    /// </summary>
    public const string VariablePattern = @"\{\{([^}]+)\}\}";

    /// <summary>
    /// Interpolates context values into a template string.
    /// </summary>
    /// <param name="template">Template string with {{variable}} placeholders.</param>
    /// <param name="context">Workflow context containing values.</param>
    /// <returns>Interpolated string.</returns>
    public static string Interpolate(string template, WorkflowContext context)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return System.Text.RegularExpressions.Regex.Replace(
            template,
            VariablePattern,
            match =>
            {
                var path = match.Groups[1].Value.Trim();
                var value = context.GetValue(path);
                return value?.ToString() ?? match.Value;
            }
        );
    }
}
