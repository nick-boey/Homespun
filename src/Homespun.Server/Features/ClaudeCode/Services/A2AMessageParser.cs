using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Provides JSON serialization options and parsing for A2A protocol messages.
/// </summary>
public static class A2AMessageParser
{
    /// <summary>
    /// Creates JsonSerializerOptions configured for A2A message deserialization.
    /// Uses camelCase naming to match the A2A protocol specification.
    /// </summary>
    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        return options;
    }

    /// <summary>
    /// Parses an A2A SSE event and returns the corresponding event object.
    /// Returns null if the event type is unknown or parsing fails.
    /// </summary>
    public static A2AEvent? ParseSseEvent(string eventKind, string data)
    {
        try
        {
            return eventKind switch
            {
                A2AEventKind.Task => JsonSerializer.Deserialize<A2ATask>(data, CreateJsonOptions()),
                A2AEventKind.Message => JsonSerializer.Deserialize<A2AMessage>(data, CreateJsonOptions()),
                A2AEventKind.StatusUpdate => JsonSerializer.Deserialize<A2ATaskStatusUpdateEvent>(data, CreateJsonOptions()),
                A2AEventKind.ArtifactUpdate => JsonSerializer.Deserialize<A2ATaskArtifactUpdateEvent>(data, CreateJsonOptions()),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if an event kind is a known A2A event type.
    /// </summary>
    public static bool IsA2AEventKind(string eventKind)
    {
        return eventKind is A2AEventKind.Task
            or A2AEventKind.Message
            or A2AEventKind.StatusUpdate
            or A2AEventKind.ArtifactUpdate;
    }

    /// <summary>
    /// Converts an A2A event to the legacy SdkMessage format for backward compatibility.
    /// This allows the existing ClaudeSessionService processing pipeline to work unchanged.
    /// </summary>
    public static SdkMessage? ConvertToSdkMessage(A2AEvent a2aEvent, string sessionId)
    {
        return a2aEvent switch
        {
            A2ATask task => ConvertTask(task, sessionId),
            A2AMessage message => ConvertMessage(message, sessionId),
            A2ATaskStatusUpdateEvent statusUpdate => ConvertStatusUpdate(statusUpdate, sessionId),
            _ => null
        };
    }

    private static SdkMessage ConvertTask(A2ATask task, string sessionId)
    {
        // Initial task event maps to session_started system message
        return new SdkSystemMessage(
            SessionId: task.Id,
            Uuid: null,
            Subtype: "session_started",
            Model: null,
            Tools: null
        );
    }

    private static SdkMessage? ConvertMessage(A2AMessage message, string sessionId)
    {
        // Extract SDK message type from metadata
        var sdkMessageType = GetMetadataString(message.Metadata, "sdkMessageType");

        // Build content blocks from parts
        var contentBlocks = ConvertPartsToContentBlocks(message.Parts);

        return sdkMessageType switch
        {
            "assistant" => new SdkAssistantMessage(
                SessionId: sessionId,
                Uuid: message.MessageId,
                Message: new SdkApiMessage(message.Role, contentBlocks),
                ParentToolUseId: GetMetadataString(message.Metadata, "parentToolUseId")
            ),
            "user" => new SdkUserMessage(
                SessionId: sessionId,
                Uuid: message.MessageId,
                Message: new SdkApiMessage(message.Role, contentBlocks),
                ParentToolUseId: GetMetadataString(message.Metadata, "parentToolUseId")
            ),
            "system" => ConvertSystemMessage(message, sessionId),
            "stream_event" => ConvertStreamEvent(message, sessionId),
            _ => sdkMessageType switch
            {
                // Fall back to role-based mapping
                _ when message.Role == "agent" => new SdkAssistantMessage(
                    SessionId: sessionId,
                    Uuid: message.MessageId,
                    Message: new SdkApiMessage("assistant", contentBlocks),
                    ParentToolUseId: null
                ),
                _ when message.Role == "user" => new SdkUserMessage(
                    SessionId: sessionId,
                    Uuid: message.MessageId,
                    Message: new SdkApiMessage("user", contentBlocks),
                    ParentToolUseId: null
                ),
                _ => null
            }
        };
    }

    private static SdkSystemMessage ConvertSystemMessage(A2AMessage message, string sessionId)
    {
        // Extract system message data from data part
        string? subtype = null;
        string? model = null;
        List<string>? tools = null;

        foreach (var part in message.Parts)
        {
            if (part is A2ADataPart dataPart)
            {
                subtype = GetJsonString(dataPart.Data, "subtype");
                model = GetJsonString(dataPart.Data, "model");

                if (dataPart.Data.TryGetProperty("tools", out var toolsElement) &&
                    toolsElement.ValueKind == JsonValueKind.Array)
                {
                    tools = new List<string>();
                    foreach (var tool in toolsElement.EnumerateArray())
                    {
                        var toolName = tool.GetString();
                        if (toolName != null) tools.Add(toolName);
                    }
                }
            }
        }

        return new SdkSystemMessage(
            SessionId: sessionId,
            Uuid: message.MessageId,
            Subtype: subtype,
            Model: model,
            Tools: tools
        );
    }

    private static SdkStreamEvent ConvertStreamEvent(A2AMessage message, string sessionId)
    {
        // Extract stream event data
        JsonElement? eventElement = null;
        foreach (var part in message.Parts)
        {
            if (part is A2ADataPart dataPart)
            {
                eventElement = dataPart.Data;
                break;
            }
        }

        return new SdkStreamEvent(
            SessionId: sessionId,
            Uuid: message.MessageId,
            Event: eventElement,
            ParentToolUseId: GetMetadataString(message.Metadata, "parentToolUseId")
        );
    }

    private static SdkMessage? ConvertStatusUpdate(A2ATaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        return statusUpdate.Status.State switch
        {
            A2ATaskState.InputRequired => ConvertInputRequired(statusUpdate, sessionId),
            A2ATaskState.Completed or A2ATaskState.Failed => ConvertResult(statusUpdate, sessionId),
            _ => null // working/submitted states don't map to SDK messages
        };
    }

    private static SdkMessage ConvertInputRequired(A2ATaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        // Check metadata for input type
        var inputType = GetMetadataString(statusUpdate.Metadata, "inputType");

        if (inputType == A2AInputType.Question)
        {
            // Extract questions from status message parts
            var questionsJson = ExtractQuestionsJson(statusUpdate);
            return new SdkQuestionPendingMessage(sessionId, questionsJson);
        }

        if (inputType == A2AInputType.PlanApproval)
        {
            // Extract plan from status message parts
            var planJson = ExtractPlanJson(statusUpdate);
            return new SdkPlanPendingMessage(sessionId, planJson);
        }

        // Default to question if unspecified
        var defaultQuestionsJson = ExtractQuestionsJson(statusUpdate);
        return new SdkQuestionPendingMessage(sessionId, defaultQuestionsJson);
    }

    private static SdkResultMessage ConvertResult(A2ATaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        var isError = statusUpdate.Status.State == A2ATaskState.Failed;

        // Extract result metadata from status message
        string? subtype = null;
        int durationMs = 0;
        int durationApiMs = 0;
        int numTurns = 0;
        decimal totalCostUsd = 0m;
        string? result = null;
        List<string>? errors = null;

        if (statusUpdate.Status.Message != null)
        {
            foreach (var part in statusUpdate.Status.Message.Parts)
            {
                if (part is A2ATextPart textPart)
                {
                    result = textPart.Text;
                }
                else if (part is A2ADataPart dataPart)
                {
                    var metadata = dataPart.Metadata;
                    if (metadata != null && metadata.TryGetValue("kind", out var kindElement) &&
                        kindElement.GetString() == "result_metadata")
                    {
                        subtype = GetJsonString(dataPart.Data, "subtype");
                        durationMs = GetJsonInt(dataPart.Data, "durationMs");
                        durationApiMs = GetJsonInt(dataPart.Data, "durationApiMs");
                        numTurns = GetJsonInt(dataPart.Data, "numTurns");
                        totalCostUsd = GetJsonDecimal(dataPart.Data, "totalCostUsd");

                        if (dataPart.Data.TryGetProperty("errors", out var errorsElement) &&
                            errorsElement.ValueKind == JsonValueKind.Array)
                        {
                            errors = new List<string>();
                            foreach (var err in errorsElement.EnumerateArray())
                            {
                                var errStr = err.GetString();
                                if (errStr != null) errors.Add(errStr);
                            }
                        }
                    }
                }
            }
        }

        return new SdkResultMessage(
            SessionId: statusUpdate.ContextId,
            Uuid: null,
            Subtype: subtype ?? (isError ? "error_during_execution" : "success"),
            DurationMs: durationMs,
            DurationApiMs: durationApiMs,
            IsError: isError,
            NumTurns: numTurns,
            TotalCostUsd: totalCostUsd,
            Result: result,
            Errors: errors
        );
    }

    private static List<SdkContentBlock> ConvertPartsToContentBlocks(List<A2APart> parts)
    {
        var blocks = new List<SdkContentBlock>();

        foreach (var part in parts)
        {
            switch (part)
            {
                case A2ATextPart textPart:
                    blocks.Add(new SdkTextBlock(textPart.Text));
                    break;

                case A2ADataPart dataPart:
                    var kind = GetMetadataString(dataPart.Metadata, "kind");
                    switch (kind)
                    {
                        case "thinking":
                            var thinking = GetJsonString(dataPart.Data, "thinking");
                            blocks.Add(new SdkThinkingBlock(thinking));
                            break;

                        case "tool_use":
                            var toolName = GetJsonString(dataPart.Data, "toolName");
                            var toolUseId = GetJsonString(dataPart.Data, "toolUseId");
                            var input = dataPart.Data.TryGetProperty("input", out var inputEl) ? inputEl : default;
                            blocks.Add(new SdkToolUseBlock(toolUseId ?? "", toolName ?? "", input));
                            break;

                        case "tool_result":
                            var resultToolUseId = GetJsonString(dataPart.Data, "toolUseId");
                            var content = dataPart.Data.TryGetProperty("content", out var contentEl) ? contentEl : default;
                            var isErr = dataPart.Data.TryGetProperty("isError", out var isErrEl) && isErrEl.GetBoolean();
                            blocks.Add(new SdkToolResultBlock(resultToolUseId ?? "", content, isErr));
                            break;
                    }
                    break;
            }
        }

        return blocks;
    }

    private static string ExtractQuestionsJson(A2ATaskStatusUpdateEvent statusUpdate)
    {
        // Try to get questions from metadata
        if (statusUpdate.Metadata != null &&
            statusUpdate.Metadata.TryGetValue("questions", out var questionsElement))
        {
            return questionsElement.GetRawText();
        }

        // Try to get from status message parts
        if (statusUpdate.Status.Message != null)
        {
            foreach (var part in statusUpdate.Status.Message.Parts)
            {
                if (part is A2ADataPart dataPart)
                {
                    var kind = GetMetadataString(dataPart.Metadata, "kind");
                    if (kind == "questions" && dataPart.Data.TryGetProperty("questions", out var questions))
                    {
                        return JsonSerializer.Serialize(new { questions });
                    }
                }
            }
        }

        return "{}";
    }

    private static string ExtractPlanJson(A2ATaskStatusUpdateEvent statusUpdate)
    {
        // Try to get plan from metadata
        if (statusUpdate.Metadata != null &&
            statusUpdate.Metadata.TryGetValue("plan", out var planElement))
        {
            return JsonSerializer.Serialize(new { plan = planElement.GetString() });
        }

        // Try to get from status message parts
        if (statusUpdate.Status.Message != null)
        {
            foreach (var part in statusUpdate.Status.Message.Parts)
            {
                if (part is A2ADataPart dataPart)
                {
                    var kind = GetMetadataString(dataPart.Metadata, "kind");
                    if (kind == "plan" && dataPart.Data.TryGetProperty("plan", out var plan))
                    {
                        return JsonSerializer.Serialize(new { plan = plan.GetString() });
                    }
                }
            }
        }

        return "{}";
    }

    private static string? GetMetadataString(Dictionary<string, JsonElement>? metadata, string key)
    {
        if (metadata == null) return null;
        if (!metadata.TryGetValue(key, out var element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetJsonInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static decimal GetJsonDecimal(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDecimal()
            : 0m;
    }
}
