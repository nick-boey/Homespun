using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <inheritdoc />
public sealed class ToolCallResultAppender : IToolCallResultAppender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISessionEventIngestor _ingestor;
    private readonly ILogger<ToolCallResultAppender> _logger;

    public ToolCallResultAppender(
        ISessionEventIngestor ingestor,
        ILogger<ToolCallResultAppender> logger)
    {
        _ingestor = ingestor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        string projectId,
        string sessionId,
        string? toolCallId,
        object resultPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(toolCallId))
        {
            _logger.LogWarning(
                "ToolCallResultAppender: skipping append for session {SessionId} — no pending toolCallId",
                sessionId);
            return;
        }

        // Serialize the result payload into a JSON element that the translator's
        // TranslateUserMessageParts path will surface as the ToolCallResult content.
        var resultElement = JsonSerializer.SerializeToElement(resultPayload, JsonOptions);

        // Synthesize an A2A user Message with a DataPart carrying metadata.kind = "tool_result".
        // This is the same wire shape the worker emits for tool results — A2AMessageParser.
        // ParseMessage deserialises it to an AgentMessage and TranslateUserMessageParts emits
        // a single ToolCallResultEvent. Routing through the ingestor guarantees persistence,
        // seq allocation, and SignalR broadcast all match the worker path exactly.
        var messageId = Guid.NewGuid().ToString();
        var a2aMessage = new Dictionary<string, object?>
        {
            ["kind"] = HomespunA2AEventKind.Message,
            ["messageId"] = messageId,
            ["role"] = "user",
            ["parts"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["kind"] = "data",
                    ["data"] = new Dictionary<string, object?>
                    {
                        ["toolUseId"] = toolCallId,
                        ["content"] = resultElement,
                    },
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["kind"] = "tool_result",
                    },
                },
            },
            ["metadata"] = new Dictionary<string, object?>
            {
                ["sdkMessageType"] = "user",
            },
        };

        var payload = JsonSerializer.SerializeToElement(a2aMessage, JsonOptions);

        await _ingestor.IngestAsync(
            projectId,
            sessionId,
            HomespunA2AEventKind.Message,
            payload,
            cancellationToken);

        _logger.LogInformation(
            "Appended synthesized TOOL_CALL_RESULT for session {SessionId} toolCallId={ToolCallId}",
            sessionId, toolCallId);
    }
}
