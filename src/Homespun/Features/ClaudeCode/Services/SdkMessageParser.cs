using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Provides JSON serialization options and converters for SdkMessage types.
/// </summary>
public static class SdkMessageParser
{
    /// <summary>
    /// Creates JsonSerializerOptions configured for SDK message deserialization.
    /// Uses snake_case naming to match the TypeScript SDK output.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new SdkMessageConverter());
        options.Converters.Add(new SdkContentBlockConverter());
        return options;
    }
}

/// <summary>
/// JSON converter for the SdkMessage discriminated union.
/// Reads the "type" field to dispatch to the correct concrete type.
/// </summary>
public class SdkMessageConverter : JsonConverter<SdkMessage>
{
    public override SdkMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString();

        return type switch
        {
            "assistant" => DeserializeAssistant(root, options),
            "user" => DeserializeUser(root, options),
            "result" => DeserializeResult(root),
            "system" => DeserializeSystem(root),
            "stream_event" => DeserializeStreamEvent(root),
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, SdkMessage value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, options);
    }

    private static SdkAssistantMessage DeserializeAssistant(JsonElement root, JsonSerializerOptions options)
    {
        var sessionId = GetString(root, "session_id") ?? "";
        var uuid = GetString(root, "uuid");
        var parentToolUseId = GetString(root, "parent_tool_use_id");
        var message = DeserializeApiMessage(root, "message", options);

        return new SdkAssistantMessage(sessionId, uuid, message, parentToolUseId);
    }

    private static SdkUserMessage DeserializeUser(JsonElement root, JsonSerializerOptions options)
    {
        var sessionId = GetString(root, "session_id") ?? "";
        var uuid = GetString(root, "uuid");
        var parentToolUseId = GetString(root, "parent_tool_use_id");
        var message = DeserializeApiMessage(root, "message", options);

        return new SdkUserMessage(sessionId, uuid, message, parentToolUseId);
    }

    private static SdkResultMessage DeserializeResult(JsonElement root)
    {
        return new SdkResultMessage(
            SessionId: GetString(root, "session_id") ?? "",
            Uuid: GetString(root, "uuid"),
            Subtype: GetString(root, "subtype"),
            DurationMs: GetInt(root, "duration_ms"),
            DurationApiMs: GetInt(root, "duration_api_ms"),
            IsError: GetBool(root, "is_error"),
            NumTurns: GetInt(root, "num_turns"),
            TotalCostUsd: GetDecimal(root, "total_cost_usd"),
            Result: GetString(root, "result")
        );
    }

    private static SdkSystemMessage DeserializeSystem(JsonElement root)
    {
        List<string>? tools = null;
        if (root.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
        {
            tools = new List<string>();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var name = tool.GetString();
                if (name != null) tools.Add(name);
            }
        }

        return new SdkSystemMessage(
            SessionId: GetString(root, "session_id") ?? "",
            Uuid: GetString(root, "uuid"),
            Subtype: GetString(root, "subtype"),
            Model: GetString(root, "model"),
            Tools: tools
        );
    }

    private static SdkStreamEvent DeserializeStreamEvent(JsonElement root)
    {
        JsonElement? eventElement = null;
        if (root.TryGetProperty("event", out var evt) && evt.ValueKind != JsonValueKind.Null)
        {
            eventElement = evt.Clone();
        }

        return new SdkStreamEvent(
            SessionId: GetString(root, "session_id") ?? "",
            Uuid: GetString(root, "uuid"),
            Event: eventElement,
            ParentToolUseId: GetString(root, "parent_tool_use_id")
        );
    }

    private static SdkApiMessage DeserializeApiMessage(JsonElement root, string propertyName, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out var msgElement))
            return new SdkApiMessage("unknown", new List<SdkContentBlock>());

        var role = GetString(msgElement, "role") ?? "unknown";
        var content = new List<SdkContentBlock>();

        if (msgElement.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var blockElement in contentArray.EnumerateArray())
            {
                var block = DeserializeContentBlock(blockElement);
                if (block != null) content.Add(block);
            }
        }

        return new SdkApiMessage(role, content);
    }

    private static SdkContentBlock? DeserializeContentBlock(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString();

        return type switch
        {
            "text" => new SdkTextBlock(GetString(element, "text")),
            "thinking" => new SdkThinkingBlock(GetString(element, "thinking")),
            "tool_use" => new SdkToolUseBlock(
                GetString(element, "id") ?? "",
                GetString(element, "name") ?? "",
                element.TryGetProperty("input", out var input) ? input.Clone() : default
            ),
            "tool_result" => new SdkToolResultBlock(
                GetString(element, "tool_use_id") ?? "",
                element.TryGetProperty("content", out var content) ? content.Clone() : default,
                element.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True ? true :
                element.TryGetProperty("is_error", out var isError2) && isError2.ValueKind == JsonValueKind.False ? false : null
            ),
            _ => null
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return false;
        return prop.ValueKind == JsonValueKind.True;
    }

    private static decimal GetDecimal(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDecimal()
            : 0m;
    }
}

/// <summary>
/// JSON converter for the SdkContentBlock discriminated union.
/// </summary>
public class SdkContentBlockConverter : JsonConverter<SdkContentBlock>
{
    public override SdkContentBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString();

        return type switch
        {
            "text" => new SdkTextBlock(
                root.TryGetProperty("text", out var text) ? text.GetString() : null),
            "thinking" => new SdkThinkingBlock(
                root.TryGetProperty("thinking", out var thinking) ? thinking.GetString() : null),
            "tool_use" => new SdkToolUseBlock(
                root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                root.TryGetProperty("input", out var input) ? input.Clone() : default),
            "tool_result" => new SdkToolResultBlock(
                root.TryGetProperty("tool_use_id", out var toolUseId) ? toolUseId.GetString() ?? "" : "",
                root.TryGetProperty("content", out var content) ? content.Clone() : default,
                root.TryGetProperty("is_error", out var isError)
                    ? isError.ValueKind == JsonValueKind.True ? true
                    : isError.ValueKind == JsonValueKind.False ? false : null
                    : null),
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, SdkContentBlock value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, options);
    }
}
