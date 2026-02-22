using System.Text.Json;
using A2A;
using Homespun.Shared.Models.Sessions;

// Note: AG-UI event types are defined in Homespun.Shared.Models.Sessions.AGUIEvents

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for translating A2A protocol events to AG-UI events.
/// This enables the server to produce standardized AG-UI events for the frontend.
/// </summary>
public interface IAGUIEventService
{
    /// <summary>
    /// Translates an A2A TaskStatusUpdateEvent to AG-UI events.
    /// </summary>
    /// <param name="statusUpdate">The A2A status update event.</param>
    /// <param name="sessionId">The session ID (used as threadId in AG-UI).</param>
    /// <param name="runId">The run ID for this execution.</param>
    /// <returns>Zero or more AG-UI events.</returns>
    IEnumerable<AGUIBaseEvent> TranslateStatusUpdate(TaskStatusUpdateEvent statusUpdate, string sessionId, string runId);

    /// <summary>
    /// Translates an A2A AgentMessage to AG-UI text message events.
    /// </summary>
    /// <param name="message">The A2A agent message.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>Zero or more AG-UI events.</returns>
    IEnumerable<AGUIBaseEvent> TranslateMessage(AgentMessage message, string sessionId);

    /// <summary>
    /// Translates an A2A AgentTask to AG-UI events.
    /// </summary>
    /// <param name="task">The A2A task.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>Zero or more AG-UI events.</returns>
    IEnumerable<AGUIBaseEvent> TranslateTask(AgentTask task, string sessionId);

    /// <summary>
    /// Creates a RunStartedEvent for a new session.
    /// </summary>
    RunStartedEvent CreateRunStarted(string sessionId, string runId);

    /// <summary>
    /// Creates a RunFinishedEvent for a completed session.
    /// </summary>
    RunFinishedEvent CreateRunFinished(string sessionId, string runId, object? result = null);

    /// <summary>
    /// Creates a RunErrorEvent for a failed session.
    /// </summary>
    RunErrorEvent CreateRunError(string message, string? code = null);

    /// <summary>
    /// Creates a custom QuestionPending event.
    /// </summary>
    CustomEvent CreateQuestionPending(PendingQuestion question);

    /// <summary>
    /// Creates a custom PlanPending event.
    /// </summary>
    CustomEvent CreatePlanPending(string planContent, string? planFilePath);
}

/// <summary>
/// Implementation of IAGUIEventService that translates A2A events to AG-UI events.
/// </summary>
public class AGUIEventService : IAGUIEventService
{
    private readonly ILogger<AGUIEventService> _logger;

    public AGUIEventService(ILogger<AGUIEventService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IEnumerable<AGUIBaseEvent> TranslateStatusUpdate(TaskStatusUpdateEvent statusUpdate, string sessionId, string runId)
    {
        var state = statusUpdate.Status.State;

        switch (state)
        {
            case TaskState.Working:
                // Working state maps to RunStarted
                yield return AGUIEventFactory.CreateRunStarted(sessionId, runId);
                break;

            case TaskState.Completed:
                // Completed state maps to RunFinished
                string? result = null;
                if (statusUpdate.Status.Message != null)
                {
                    result = ExtractTextFromMessage(statusUpdate.Status.Message);
                }
                yield return AGUIEventFactory.CreateRunFinished(sessionId, runId, result);
                break;

            case TaskState.Failed:
                // Failed state maps to RunError
                var errorMessage = "Task failed";
                if (statusUpdate.Status.Message != null)
                {
                    errorMessage = ExtractTextFromMessage(statusUpdate.Status.Message) ?? errorMessage;
                }
                yield return AGUIEventFactory.CreateRunError(errorMessage);
                break;

            case TaskState.InputRequired:
                // Input-required needs context to determine if it's a question or plan
                var inputType = statusUpdate.Metadata.GetMetadataString("inputType");

                if (inputType == A2AInputType.PlanApproval)
                {
                    var planContent = ExtractPlanContent(statusUpdate);
                    var planFilePath = statusUpdate.Metadata.GetMetadataString("planFilePath");
                    yield return CreatePlanPending(planContent, planFilePath);
                }
                else
                {
                    // Default to question (inputType == "question" or unspecified)
                    var question = ExtractQuestion(statusUpdate);
                    if (question != null)
                    {
                        yield return CreateQuestionPending(question);
                    }
                }
                break;

            case TaskState.Canceled:
                yield return AGUIEventFactory.CreateRunError("Task was canceled", "canceled");
                break;

            default:
                _logger.LogDebug("Unhandled A2A task state {State} for session {SessionId}", state, sessionId);
                break;
        }
    }

    /// <inheritdoc />
    public IEnumerable<AGUIBaseEvent> TranslateMessage(AgentMessage message, string sessionId)
    {
        var messageId = message.MessageId ?? Guid.NewGuid().ToString();

        // Check metadata for SDK message type hints
        var sdkMessageType = message.Metadata.GetMetadataString("sdkMessageType");

        // For assistant messages, emit text message events
        if (message.Role == MessageRole.Agent || sdkMessageType == "assistant")
        {
            // Start the message
            yield return AGUIEventFactory.CreateTextMessageStart(messageId, "assistant");

            // Process parts
            foreach (var part in message.Parts ?? [])
            {
                if (part is TextPart textPart && !string.IsNullOrEmpty(textPart.Text))
                {
                    yield return AGUIEventFactory.CreateTextMessageContent(messageId, textPart.Text);
                }
                else if (part is DataPart dataPart)
                {
                    var kind = dataPart.Metadata.GetMetadataString("kind");

                    switch (kind)
                    {
                        case "tool_use":
                            var toolCallId = dataPart.GetDataString("toolUseId") ?? Guid.NewGuid().ToString();
                            var toolName = dataPart.GetDataString("toolName") ?? "unknown";
                            yield return AGUIEventFactory.CreateToolCallStart(toolCallId, toolName, messageId);

                            // If there's input, emit tool call args
                            var input = dataPart.GetDataElement("input");
                            if (input.HasValue)
                            {
                                yield return AGUIEventFactory.CreateToolCallArgs(toolCallId, input.Value.ToString());
                            }

                            yield return AGUIEventFactory.CreateToolCallEnd(toolCallId);
                            break;

                        case "thinking":
                            // Thinking content is still emitted as text content
                            var thinking = dataPart.GetDataString("thinking");
                            if (!string.IsNullOrEmpty(thinking))
                            {
                                yield return AGUIEventFactory.CreateTextMessageContent(messageId, thinking);
                            }
                            break;
                    }
                }
            }

            // End the message
            yield return AGUIEventFactory.CreateTextMessageEnd(messageId);
        }
        else if (message.Role == MessageRole.User || sdkMessageType == "user")
        {
            // For user messages (tool results), emit tool result events
            foreach (var part in message.Parts ?? [])
            {
                if (part is DataPart dataPart)
                {
                    var kind = dataPart.Metadata.GetMetadataString("kind");
                    if (kind == "tool_result")
                    {
                        var toolUseId = dataPart.GetDataString("toolUseId") ?? "";
                        var content = dataPart.GetDataElement("content");
                        var contentStr = content?.ToString() ?? "";
                        yield return AGUIEventFactory.CreateToolCallResult(toolUseId, contentStr, messageId);
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<AGUIBaseEvent> TranslateTask(AgentTask task, string sessionId)
    {
        // Initial task event signals session started
        var runId = task.Id ?? Guid.NewGuid().ToString();
        yield return AGUIEventFactory.CreateRunStarted(sessionId, runId);
    }

    /// <inheritdoc />
    public RunStartedEvent CreateRunStarted(string sessionId, string runId)
    {
        return AGUIEventFactory.CreateRunStarted(sessionId, runId);
    }

    /// <inheritdoc />
    public RunFinishedEvent CreateRunFinished(string sessionId, string runId, object? result = null)
    {
        return AGUIEventFactory.CreateRunFinished(sessionId, runId, result);
    }

    /// <inheritdoc />
    public RunErrorEvent CreateRunError(string message, string? code = null)
    {
        return AGUIEventFactory.CreateRunError(message, code);
    }

    /// <inheritdoc />
    public CustomEvent CreateQuestionPending(PendingQuestion question)
    {
        return AGUIEventFactory.CreateQuestionPending(question);
    }

    /// <inheritdoc />
    public CustomEvent CreatePlanPending(string planContent, string? planFilePath)
    {
        return AGUIEventFactory.CreatePlanPending(planContent, planFilePath);
    }

    /// <summary>
    /// Extracts text content from an A2A message.
    /// </summary>
    private static string? ExtractTextFromMessage(AgentMessage message)
    {
        foreach (var part in message.Parts ?? [])
        {
            if (part is TextPart textPart)
            {
                return textPart.Text;
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts a PendingQuestion from an A2A input-required status update.
    /// </summary>
    private PendingQuestion? ExtractQuestion(TaskStatusUpdateEvent statusUpdate)
    {
        // Try to get toolUseId from metadata
        var toolUseId = statusUpdate.Metadata.GetMetadataString("toolUseId");

        // Try to get questions from metadata
        if (statusUpdate.Metadata != null &&
            statusUpdate.Metadata.TryGetValue("questions", out var questionsElement))
        {
            try
            {
                var questions = JsonSerializer.Deserialize<List<A2AQuestionData>>(questionsElement.GetRawText());
                if (questions != null && questions.Count > 0)
                {
                    return ConvertA2AQuestionsToPendingQuestion(questions, toolUseId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize questions from A2A metadata");
            }
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
                        try
                        {
                            var dataQuestionsElement = dataPart.GetDataElement("questions");
                            if (dataQuestionsElement != null)
                            {
                                var questions = JsonSerializer.Deserialize<List<A2AQuestionData>>(dataQuestionsElement.Value.GetRawText());
                                if (questions != null && questions.Count > 0)
                                {
                                    return ConvertA2AQuestionsToPendingQuestion(questions, toolUseId);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize questions from A2A data part");
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Converts A2A question data to a PendingQuestion.
    /// </summary>
    private static PendingQuestion ConvertA2AQuestionsToPendingQuestion(List<A2AQuestionData> questions, string? toolUseId = null)
    {
        var userQuestions = questions.Select(q => new UserQuestion
        {
            Question = q.Question,
            Header = q.Header,
            Options = q.Options.Select(o => new QuestionOption
            {
                Label = o.Label,
                Description = o.Description
            }).ToList(),
            MultiSelect = q.MultiSelect
        }).ToList();

        return new PendingQuestion
        {
            Id = Guid.NewGuid().ToString(),
            ToolUseId = toolUseId ?? Guid.NewGuid().ToString(),
            Questions = userQuestions
        };
    }

    /// <summary>
    /// Extracts plan content from an A2A input-required status update.
    /// </summary>
    private string ExtractPlanContent(TaskStatusUpdateEvent statusUpdate)
    {
        // Try to get plan from metadata
        if (statusUpdate.Metadata != null &&
            statusUpdate.Metadata.TryGetValue("plan", out var planElement))
        {
            return planElement.GetString() ?? "";
        }

        // Try to get from status message parts
        if (statusUpdate.Status.Message != null)
        {
            foreach (var part in statusUpdate.Status.Message.Parts ?? [])
            {
                if (part is DataPart dataPart)
                {
                    var kind = dataPart.Metadata.GetMetadataString("kind");
                    if (kind == "plan")
                    {
                        return dataPart.GetDataString("plan") ?? "";
                    }
                }
                else if (part is TextPart textPart)
                {
                    // Plan content might be in a text part
                    return textPart.Text ?? "";
                }
            }
        }

        return "";
    }
}
