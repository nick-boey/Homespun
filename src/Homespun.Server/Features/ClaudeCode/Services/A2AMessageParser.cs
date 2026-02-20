using System.Text.Json;
using System.Text.Json.Serialization;
using A2A;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Result type for parsed A2A events. Since SDK types don't have a common base,
/// this wrapper provides a unified way to handle different event types.
/// </summary>
public abstract record ParsedA2AEvent;

public sealed record ParsedAgentTask(AgentTask Task) : ParsedA2AEvent;
public sealed record ParsedAgentMessage(AgentMessage Message) : ParsedA2AEvent;
public sealed record ParsedTaskStatusUpdateEvent(TaskStatusUpdateEvent StatusUpdate) : ParsedA2AEvent;
public sealed record ParsedTaskArtifactUpdateEvent(TaskArtifactUpdateEvent ArtifactUpdate) : ParsedA2AEvent;

/// <summary>
/// Provides JSON serialization options and parsing for A2A protocol messages.
/// Uses the A2A SDK types for deserialization.
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
    public static ParsedA2AEvent? ParseSseEvent(string eventKind, string data)
    {
        try
        {
            return eventKind switch
            {
                HomespunA2AEventKind.Task => ParseTask(data),
                HomespunA2AEventKind.Message => ParseMessage(data),
                HomespunA2AEventKind.StatusUpdate => ParseStatusUpdate(data),
                HomespunA2AEventKind.ArtifactUpdate => ParseArtifactUpdate(data),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedAgentTask? ParseTask(string data)
    {
        var task = JsonSerializer.Deserialize<AgentTask>(data, CreateJsonOptions());
        return task != null ? new ParsedAgentTask(task) : null;
    }

    private static ParsedAgentMessage? ParseMessage(string data)
    {
        var message = JsonSerializer.Deserialize<AgentMessage>(data, CreateJsonOptions());
        return message != null ? new ParsedAgentMessage(message) : null;
    }

    private static ParsedTaskStatusUpdateEvent? ParseStatusUpdate(string data)
    {
        var statusUpdate = JsonSerializer.Deserialize<TaskStatusUpdateEvent>(data, CreateJsonOptions());
        return statusUpdate != null ? new ParsedTaskStatusUpdateEvent(statusUpdate) : null;
    }

    private static ParsedTaskArtifactUpdateEvent? ParseArtifactUpdate(string data)
    {
        var artifactUpdate = JsonSerializer.Deserialize<TaskArtifactUpdateEvent>(data, CreateJsonOptions());
        return artifactUpdate != null ? new ParsedTaskArtifactUpdateEvent(artifactUpdate) : null;
    }

    /// <summary>
    /// Checks if an event kind is a known A2A event type.
    /// </summary>
    public static bool IsA2AEventKind(string eventKind)
    {
        return eventKind is HomespunA2AEventKind.Task
            or HomespunA2AEventKind.Message
            or HomespunA2AEventKind.StatusUpdate
            or HomespunA2AEventKind.ArtifactUpdate;
    }

    /// <summary>
    /// Converts an A2A event to the legacy SdkMessage format for backward compatibility.
    /// This allows the existing ClaudeSessionService processing pipeline to work unchanged.
    /// </summary>
    public static SdkMessage? ConvertToSdkMessage(ParsedA2AEvent a2aEvent, string sessionId)
    {
        return a2aEvent switch
        {
            ParsedAgentTask parsed => ConvertTask(parsed.Task, sessionId),
            ParsedAgentMessage parsed => ConvertMessage(parsed.Message, sessionId),
            ParsedTaskStatusUpdateEvent parsed => ConvertStatusUpdate(parsed.StatusUpdate, sessionId),
            _ => null
        };
    }

    private static SdkMessage ConvertTask(AgentTask task, string sessionId)
    {
        // Initial task event maps to session_started system message
        return new SdkSystemMessage(
            SessionId: task.Id ?? sessionId,
            Uuid: null,
            Subtype: "session_started",
            Model: null,
            Tools: null
        );
    }

    private static SdkMessage? ConvertMessage(AgentMessage message, string sessionId)
    {
        // Extract SDK message type from metadata
        var sdkMessageType = message.Metadata.GetMetadataString("sdkMessageType");

        // Build content blocks from parts
        var contentBlocks = ConvertPartsToContentBlocks(message.Parts);

        // Get the role as a string for SdkApiMessage
        var roleString = message.Role.ToSdkRole();

        return sdkMessageType switch
        {
            "assistant" => new SdkAssistantMessage(
                SessionId: sessionId,
                Uuid: message.MessageId,
                Message: new SdkApiMessage(roleString, contentBlocks),
                ParentToolUseId: message.Metadata.GetMetadataString("parentToolUseId")
            ),
            "user" => new SdkUserMessage(
                SessionId: sessionId,
                Uuid: message.MessageId,
                Message: new SdkApiMessage(roleString, contentBlocks),
                ParentToolUseId: message.Metadata.GetMetadataString("parentToolUseId")
            ),
            "system" => ConvertSystemMessage(message, sessionId),
            "stream_event" => ConvertStreamEvent(message, sessionId),
            _ => message.Role switch
            {
                // Fall back to role-based mapping
                MessageRole.Agent => new SdkAssistantMessage(
                    SessionId: sessionId,
                    Uuid: message.MessageId,
                    Message: new SdkApiMessage("assistant", contentBlocks),
                    ParentToolUseId: null
                ),
                MessageRole.User => new SdkUserMessage(
                    SessionId: sessionId,
                    Uuid: message.MessageId,
                    Message: new SdkApiMessage("user", contentBlocks),
                    ParentToolUseId: null
                ),
                _ => null
            }
        };
    }

    private static SdkSystemMessage ConvertSystemMessage(AgentMessage message, string sessionId)
    {
        // Extract system message data from data part
        string? subtype = null;
        string? model = null;
        List<string>? tools = null;

        foreach (var part in message.Parts ?? [])
        {
            if (part is DataPart dataPart)
            {
                subtype = dataPart.GetDataString("subtype");
                model = dataPart.GetDataString("model");

                if (dataPart.HasDataProperty("tools"))
                {
                    var toolsElement = dataPart.GetDataElement("tools");
                    if (toolsElement?.ValueKind == JsonValueKind.Array)
                    {
                        tools = new List<string>();
                        foreach (var tool in toolsElement.Value.EnumerateArray())
                        {
                            var toolName = tool.GetString();
                            if (toolName != null) tools.Add(toolName);
                        }
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

    private static SdkStreamEvent ConvertStreamEvent(AgentMessage message, string sessionId)
    {
        // Extract stream event data
        JsonElement? eventElement = null;
        foreach (var part in message.Parts ?? [])
        {
            if (part is DataPart dataPart)
            {
                eventElement = dataPart.ToJsonElement();
                break;
            }
        }

        return new SdkStreamEvent(
            SessionId: sessionId,
            Uuid: message.MessageId,
            Event: eventElement,
            ParentToolUseId: message.Metadata.GetMetadataString("parentToolUseId")
        );
    }

    private static SdkMessage? ConvertStatusUpdate(TaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        return statusUpdate.Status.State switch
        {
            TaskState.InputRequired => ConvertInputRequired(statusUpdate, sessionId),
            TaskState.Completed or TaskState.Failed => ConvertResult(statusUpdate, sessionId),
            _ => null // working/submitted states don't map to SDK messages
        };
    }

    private static SdkMessage ConvertInputRequired(TaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        // Check metadata for input type
        var inputType = statusUpdate.Metadata.GetMetadataString("inputType");

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

    private static SdkResultMessage ConvertResult(TaskStatusUpdateEvent statusUpdate, string sessionId)
    {
        var isError = statusUpdate.Status.State == TaskState.Failed;

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
            foreach (var part in statusUpdate.Status.Message.Parts ?? [])
            {
                if (part is TextPart textPart)
                {
                    result = textPart.Text;
                }
                else if (part is DataPart dataPart)
                {
                    var metadata = dataPart.Metadata;
                    if (metadata != null && metadata.TryGetValue("kind", out var kindElement) &&
                        kindElement.GetString() == "result_metadata")
                    {
                        subtype = dataPart.GetDataString("subtype");
                        durationMs = dataPart.GetDataInt("durationMs");
                        durationApiMs = dataPart.GetDataInt("durationApiMs");
                        numTurns = dataPart.GetDataInt("numTurns");
                        totalCostUsd = dataPart.GetDataDecimal("totalCostUsd");

                        if (dataPart.HasDataProperty("errors"))
                        {
                            var errorsElement = dataPart.GetDataElement("errors");
                            if (errorsElement?.ValueKind == JsonValueKind.Array)
                            {
                                errors = new List<string>();
                                foreach (var err in errorsElement.Value.EnumerateArray())
                                {
                                    var errStr = err.GetString();
                                    if (errStr != null) errors.Add(errStr);
                                }
                            }
                        }
                    }
                }
            }
        }

        return new SdkResultMessage(
            SessionId: statusUpdate.ContextId ?? sessionId,
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

    private static List<SdkContentBlock> ConvertPartsToContentBlocks(IReadOnlyList<Part>? parts)
    {
        var blocks = new List<SdkContentBlock>();

        foreach (var part in parts ?? [])
        {
            switch (part)
            {
                case TextPart textPart:
                    blocks.Add(new SdkTextBlock(textPart.Text ?? ""));
                    break;

                case DataPart dataPart:
                    var kind = dataPart.Metadata.GetMetadataString("kind");
                    switch (kind)
                    {
                        case "thinking":
                            var thinking = dataPart.GetDataString("thinking");
                            blocks.Add(new SdkThinkingBlock(thinking));
                            break;

                        case "tool_use":
                            var toolName = dataPart.GetDataString("toolName");
                            var toolUseId = dataPart.GetDataString("toolUseId");
                            var input = dataPart.GetDataElement("input") ?? default;
                            blocks.Add(new SdkToolUseBlock(toolUseId ?? "", toolName ?? "", input));
                            break;

                        case "tool_result":
                            var resultToolUseId = dataPart.GetDataString("toolUseId");
                            var content = dataPart.GetDataElement("content") ?? default;
                            var isErr = dataPart.GetDataBool("isError");
                            blocks.Add(new SdkToolResultBlock(resultToolUseId ?? "", content, isErr));
                            break;
                    }
                    break;
            }
        }

        return blocks;
    }

    private static string ExtractQuestionsJson(TaskStatusUpdateEvent statusUpdate)
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
            foreach (var part in statusUpdate.Status.Message.Parts ?? [])
            {
                if (part is DataPart dataPart)
                {
                    var kind = dataPart.Metadata.GetMetadataString("kind");
                    if (kind == "questions" && dataPart.HasDataProperty("questions"))
                    {
                        var questions = dataPart.GetDataElement("questions");
                        if (questions != null)
                        {
                            return JsonSerializer.Serialize(new { questions = questions.Value });
                        }
                    }
                }
            }
        }

        return "{}";
    }

    private static string ExtractPlanJson(TaskStatusUpdateEvent statusUpdate)
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
            foreach (var part in statusUpdate.Status.Message.Parts ?? [])
            {
                if (part is DataPart dataPart)
                {
                    var kind = dataPart.Metadata.GetMetadataString("kind");
                    if (kind == "plan" && dataPart.HasDataProperty("plan"))
                    {
                        var plan = dataPart.GetDataString("plan");
                        return JsonSerializer.Serialize(new { plan });
                    }
                }
            }
        }

        return "{}";
    }
}
