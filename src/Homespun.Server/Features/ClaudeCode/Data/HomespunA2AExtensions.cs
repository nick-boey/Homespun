using System.Text.Json;
using System.Text.Json.Serialization;
using A2A;

namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// A2A Protocol event kind constants.
/// The SDK A2AEventKind is internal, so we maintain our own constants for use in SSE event type matching.
/// See: https://a2a-protocol.org/latest/specification/
/// </summary>
public static class HomespunA2AEventKind
{
    public const string Task = "task";
    public const string Message = "message";
    public const string StatusUpdate = "status-update";
    public const string ArtifactUpdate = "artifact-update";
}

/// <summary>
/// A2A Task state string constants for comparison with TaskState enum.
/// Used for SSE parsing before full deserialization.
/// </summary>
public static class HomespunA2ATaskState
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string InputRequired = "input-required";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Rejected = "rejected";
    public const string AuthRequired = "auth-required";
    public const string Unknown = "unknown";

    /// <summary>
    /// Converts a TaskState enum to its string representation.
    /// </summary>
    public static string ToString(TaskState state) => state switch
    {
        TaskState.Submitted => Submitted,
        TaskState.Working => Working,
        TaskState.InputRequired => InputRequired,
        TaskState.Completed => Completed,
        TaskState.Failed => Failed,
        TaskState.Canceled => Canceled,
        _ => Unknown
    };
}

/// <summary>
/// Input required metadata types for A2A input-required state.
/// </summary>
public static class A2AInputType
{
    public const string Question = "question";
    public const string PlanApproval = "plan-approval";
}

/// <summary>
/// Homespun-specific metadata for questions in input-required state.
/// </summary>
public record A2AInputRequiredMetadata
{
    [JsonPropertyName("inputType")]
    public required string InputType { get; init; }

    [JsonPropertyName("questions")]
    public List<A2AQuestionData>? Questions { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }
}

/// <summary>
/// Question data for A2A input-required events.
/// </summary>
public record A2AQuestionData
{
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("header")]
    public required string Header { get; init; }

    [JsonPropertyName("options")]
    public required List<A2AQuestionOption> Options { get; init; }

    [JsonPropertyName("multiSelect")]
    public required bool MultiSelect { get; init; }
}

/// <summary>
/// Question option for A2A questions.
/// </summary>
public record A2AQuestionOption
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>
/// Homespun-specific message metadata.
/// </summary>
public record A2AHomespunMessageMetadata
{
    [JsonPropertyName("sdkMessageType")]
    public string? SdkMessageType { get; init; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; init; }

    [JsonPropertyName("toolUseId")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("isThinking")]
    public bool? IsThinking { get; init; }

    [JsonPropertyName("isStreaming")]
    public bool? IsStreaming { get; init; }

    [JsonPropertyName("parentToolUseId")]
    public string? ParentToolUseId { get; init; }
}

/// <summary>
/// Extension methods for A2A SDK types.
/// </summary>
public static class A2AExtensions
{
    /// <summary>
    /// Gets a string value from metadata dictionary.
    /// </summary>
    public static string? GetMetadataString(this IReadOnlyDictionary<string, JsonElement>? metadata, string key)
    {
        if (metadata == null) return null;
        if (!metadata.TryGetValue(key, out var element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary>
    /// Gets a string value from a DataPart's data dictionary.
    /// </summary>
    public static string? GetDataString(this DataPart dataPart, string key)
    {
        if (dataPart.Data == null) return null;
        if (!dataPart.Data.TryGetValue(key, out var element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary>
    /// Gets an integer value from a DataPart's data dictionary.
    /// </summary>
    public static int GetDataInt(this DataPart dataPart, string key)
    {
        if (dataPart.Data == null) return 0;
        if (!dataPart.Data.TryGetValue(key, out var element)) return 0;
        return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : 0;
    }

    /// <summary>
    /// Gets a decimal value from a DataPart's data dictionary.
    /// </summary>
    public static decimal GetDataDecimal(this DataPart dataPart, string key)
    {
        if (dataPart.Data == null) return 0m;
        if (!dataPart.Data.TryGetValue(key, out var element)) return 0m;
        return element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : 0m;
    }

    /// <summary>
    /// Gets a boolean value from a DataPart's data dictionary.
    /// </summary>
    public static bool GetDataBool(this DataPart dataPart, string key)
    {
        if (dataPart.Data == null) return false;
        if (!dataPart.Data.TryGetValue(key, out var element)) return false;
        return element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False
            ? element.GetBoolean()
            : false;
    }

    /// <summary>
    /// Gets a JsonElement value from a DataPart's data dictionary.
    /// </summary>
    public static JsonElement? GetDataElement(this DataPart dataPart, string key)
    {
        if (dataPart.Data == null) return null;
        if (!dataPart.Data.TryGetValue(key, out var element)) return null;
        return element;
    }

    /// <summary>
    /// Checks if the data dictionary has a specific property.
    /// </summary>
    public static bool HasDataProperty(this DataPart dataPart, string key)
    {
        return dataPart.Data?.ContainsKey(key) ?? false;
    }

    /// <summary>
    /// Gets the data dictionary as a JsonElement by serializing and deserializing.
    /// Useful for passing to legacy code expecting JsonElement.
    /// </summary>
    public static JsonElement ToJsonElement(this DataPart dataPart)
    {
        if (dataPart.Data == null)
            return default;

        var json = JsonSerializer.Serialize(dataPart.Data);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Gets a string array from metadata dictionary.
    /// </summary>
    public static List<string>? GetMetadataStringArray(this IReadOnlyDictionary<string, JsonElement>? metadata, string key)
    {
        if (metadata == null) return null;
        if (!metadata.TryGetValue(key, out var element)) return null;
        if (element.ValueKind != JsonValueKind.Array) return null;

        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var str = item.GetString();
            if (str != null) result.Add(str);
        }
        return result;
    }

    /// <summary>
    /// Converts the A2A Role enum to the role string used in SDK messages.
    /// </summary>
    public static string ToSdkRole(this MessageRole role) => role switch
    {
        MessageRole.Agent => "assistant",
        MessageRole.User => "user",
        _ => role.ToString().ToLowerInvariant()
    };
}
