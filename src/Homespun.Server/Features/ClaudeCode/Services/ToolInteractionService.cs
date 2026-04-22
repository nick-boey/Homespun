using System.Text.Json;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using AGUIEvents = Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles question/answer flow, plan management,
/// and ExitPlanMode processing.
/// </summary>
public class ToolInteractionService : IToolInteractionService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<ToolInteractionService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly IAGUIEventService _agUIEventService;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly ISessionStateManager _stateManager;
    private readonly IPendingToolCallRegistry _pendingToolCalls;
    private readonly IToolCallResultAppender _toolCallResultAppender;
    private readonly Lazy<IMessageProcessingService> _messageProcessing;
    private readonly Lazy<ISessionLifecycleService> _lifecycle;

    public ToolInteractionService(
        IClaudeSessionStore sessionStore,
        ILogger<ToolInteractionService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IAGUIEventService agUIEventService,
        IAgentExecutionService agentExecutionService,
        ISessionStateManager stateManager,
        IPendingToolCallRegistry pendingToolCalls,
        IToolCallResultAppender toolCallResultAppender,
        Lazy<IMessageProcessingService> messageProcessing,
        Lazy<ISessionLifecycleService> lifecycle)
    {
        _sessionStore = sessionStore;
        _logger = logger;
        _hubContext = hubContext;
        _agUIEventService = agUIEventService;
        _agentExecutionService = agentExecutionService;
        _stateManager = stateManager;
        _pendingToolCalls = pendingToolCalls;
        _toolCallResultAppender = toolCallResultAppender;
        _messageProcessing = messageProcessing;
        _lifecycle = lifecycle;
    }

    public async Task HandleAskUserQuestionTool(
        string sessionId,
        ClaudeSession session,
        string toolUseId,
        string? toolInputJson,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("AskUserQuestion tool detected in session {SessionId}, parsing questions", sessionId);

        if (string.IsNullOrEmpty(toolInputJson))
        {
            _logger.LogWarning("AskUserQuestion tool had empty input for session {SessionId}", sessionId);
            return;
        }

        try
        {
            var toolInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInputJson);
            if (toolInput == null || !toolInput.TryGetValue("questions", out var questionsElement))
            {
                _logger.LogWarning("AskUserQuestion tool input missing 'questions' array");
                return;
            }

            var questions = ParseQuestions(questionsElement);
            if (questions.Count == 0)
            {
                _logger.LogWarning("AskUserQuestion tool had no questions to parse");
                return;
            }

            var pendingQuestion = new PendingQuestion
            {
                Id = Guid.NewGuid().ToString(),
                ToolUseId = toolUseId ?? "",
                Questions = questions
            };

            session.PendingQuestion = pendingQuestion;
            session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

            // The ask_user_question tool call is emitted on the canonical A2A path via
            // A2AToAGUITranslator.BuildInputRequired — do not re-broadcast it here.
            await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForQuestionAnswer);

            _logger.LogInformation("Session {SessionId} is now waiting for user to answer {QuestionCount} questions",
                sessionId, questions.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AskUserQuestion tool input in session {SessionId}", sessionId);
        }
    }

    public async Task HandleQuestionPendingFromWorkerAsync(
        string sessionId,
        ClaudeSession session,
        SdkQuestionPendingMessage questionMsg,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        if (!_stateManager.IsTurnActive(sessionId, turnId))
        {
            _logger.LogDebug("Ignoring question_pending for session {SessionId}: from superseded message turn", sessionId);
            return;
        }

        _logger.LogInformation("question_pending control event received for session {SessionId}", sessionId);

        try
        {
            using var doc = JsonDocument.Parse(questionMsg.QuestionsJson);
            var questionsElement = doc.RootElement.TryGetProperty("questions", out var qe) ? qe : doc.RootElement;

            var arrayToEnumerate = questionsElement.ValueKind == JsonValueKind.Array
                ? questionsElement
                : throw new JsonException("Expected questions array in question_pending data");

            var questions = ParseQuestions(arrayToEnumerate);
            if (questions.Count == 0)
            {
                _logger.LogWarning("question_pending event had no questions for session {SessionId}", sessionId);
                return;
            }

            var pendingQuestion = new PendingQuestion
            {
                Id = Guid.NewGuid().ToString(),
                ToolUseId = "",
                Questions = questions
            };

            session.PendingQuestion = pendingQuestion;
            session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

            // The ask_user_question tool call is emitted on the canonical A2A path via
            // A2AToAGUITranslator.BuildInputRequired — do not re-broadcast it here.
            await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForQuestionAnswer);

            _logger.LogInformation("Session {SessionId} is now waiting for user to answer {QuestionCount} questions (from worker)",
                sessionId, questions.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse question_pending JSON for session {SessionId}", sessionId);
        }
    }

    public async Task HandlePlanPendingFromWorkerAsync(
        string sessionId,
        ClaudeSession session,
        SdkPlanPendingMessage planMsg,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("plan_pending control event received for session {SessionId}", sessionId);

        if (!_stateManager.IsTurnActive(sessionId, turnId))
        {
            _logger.LogDebug("Ignoring plan_pending for session {SessionId}: from superseded message turn", sessionId);
            return;
        }

        if (session.Status == ClaudeSessionStatus.WaitingForPlanExecution)
        {
            _logger.LogDebug("Ignoring plan_pending for session {SessionId}: already waiting for plan execution", sessionId);
            return;
        }
        if (session.Status == ClaudeSessionStatus.Running &&
            session.PlanHasBeenApproved &&
            !session.HasPendingPlanApproval)
        {
            _logger.LogDebug("Ignoring plan_pending for session {SessionId}: plan already approved and executing", sessionId);
            return;
        }

        try
        {
            string? planContent = null;
            using var doc = JsonDocument.Parse(planMsg.PlanJson);
            if (doc.RootElement.TryGetProperty("plan", out var planElement))
            {
                planContent = planElement.GetString();
            }

            if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanContent))
            {
                planContent = session.PlanContent;
                _logger.LogInformation("plan_pending: Using stored plan content for session {SessionId}", sessionId);
            }

            var planAgentSessionId = _stateManager.GetAgentSessionId(sessionId);
            if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanFilePath) &&
                planAgentSessionId != null)
            {
                planContent = await _agentExecutionService.ReadFileFromAgentAsync(
                    planAgentSessionId, session.PlanFilePath, cancellationToken);
            }

            if (string.IsNullOrEmpty(planContent) && planAgentSessionId != null)
            {
                planContent = await TryReadPlanFromAgentAsync(planAgentSessionId, session.WorkingDirectory, cancellationToken);
            }

            if (!string.IsNullOrEmpty(planContent))
            {
                session.PlanContent = planContent;
                // The propose_plan tool call is emitted on the canonical A2A path via
                // A2AToAGUITranslator.BuildInputRequired — do not re-broadcast it here.
            }
            else
            {
                _logger.LogWarning("plan_pending: No plan content found for session {SessionId}", sessionId);
            }

            session.Status = ClaudeSessionStatus.WaitingForPlanExecution;
            session.HasPendingPlanApproval = true;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, session.HasPendingPlanApproval);

            _logger.LogInformation("Session {SessionId} is now waiting for plan approval (from worker plan_pending)",
                sessionId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse plan_pending JSON for session {SessionId}", sessionId);
        }
    }

    public async Task HandleExitPlanModeCompletedAsync(
        string sessionId,
        ClaudeSession session,
        string? toolInputJson,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExitPlanMode detected for session {SessionId}", sessionId);

        string? planFilePath = null;
        if (!string.IsNullOrEmpty(toolInputJson))
        {
            try
            {
                var inputParams = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInputJson);
                planFilePath = TryGetPlanFilePath(inputParams, session.WorkingDirectory);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ExitPlanMode input JSON");
            }
        }

        if (string.IsNullOrEmpty(planFilePath) && !string.IsNullOrEmpty(session.PlanFilePath))
        {
            planFilePath = session.PlanFilePath;
        }

        var (foundPath, planContent) = await TryReadPlanFileAsync(session.WorkingDirectory, planFilePath);

        if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanContent))
        {
            planContent = session.PlanContent;
            foundPath = session.PlanFilePath;
            _logger.LogInformation("ExitPlanMode: Using stored plan content for session {SessionId}", sessionId);
        }

        var exitPlanAgentId = _stateManager.GetAgentSessionId(sessionId);
        if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(planFilePath) &&
            exitPlanAgentId != null)
        {
            _logger.LogInformation("ExitPlanMode: Attempting to read plan from agent container at {Path} for session {SessionId}",
                planFilePath, sessionId);

            planContent = await _agentExecutionService.ReadFileFromAgentAsync(exitPlanAgentId, planFilePath, cancellationToken);
            if (!string.IsNullOrEmpty(planContent))
            {
                foundPath = planFilePath;
                _logger.LogInformation("ExitPlanMode: Successfully read plan from agent container ({Length} chars)", planContent.Length);
            }
        }

        if (string.IsNullOrEmpty(planContent) && exitPlanAgentId != null)
        {
            planContent = await TryReadPlanFromAgentAsync(exitPlanAgentId, session.WorkingDirectory, cancellationToken);
            if (!string.IsNullOrEmpty(planContent))
            {
                foundPath = "agent:~/.claude/plans/";
                _logger.LogInformation("ExitPlanMode: Found plan via agent container search ({Length} chars)", planContent.Length);
            }
        }

        if (!string.IsNullOrEmpty(planContent))
        {
            session.PlanFilePath = foundPath;
            session.PlanContent = planContent;

            // The propose_plan tool call is emitted on the canonical A2A path via
            // A2AToAGUITranslator.BuildInputRequired — do not re-broadcast it here.

            session.Status = ClaudeSessionStatus.WaitingForPlanExecution;
            session.HasPendingPlanApproval = true;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, session.HasPendingPlanApproval);

            _logger.LogInformation("ExitPlanMode: Displayed plan from {FilePath} for session {SessionId} ({Length} chars), awaiting execution",
                foundPath ?? "stored content", sessionId, planContent.Length);
        }
        else
        {
            _logger.LogWarning("ExitPlanMode: No plan file found for session {SessionId}", sessionId);
        }
    }

    public async Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != ClaudeSessionStatus.WaitingForQuestionAnswer || session.PendingQuestion == null)
        {
            throw new InvalidOperationException($"Session {sessionId} is not waiting for a question answer");
        }

        _logger.LogInformation("Answering question in session {SessionId} with {AnswerCount} answers",
            sessionId, answers.Count);

        var pendingQuestion = session.PendingQuestion;
        session.PendingQuestion = null;
        session.Status = ClaudeSessionStatus.Running;

        await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.Running);

        var answerAgentSessionId = _stateManager.GetAgentSessionId(sessionId);
        if (answerAgentSessionId != null)
        {
            var resolved = await _agentExecutionService.AnswerQuestionAsync(
                answerAgentSessionId, answers, cancellationToken);
            if (resolved)
            {
                _logger.LogInformation("AnswerQuestionAsync: Worker resolved question for session {SessionId}", sessionId);
                await AppendInteractiveToolResultAsync(session, answers, cancellationToken);
                return;
            }
        }

        // Fallback for local mode
        var formattedAnswers = new System.Text.StringBuilder();
        formattedAnswers.AppendLine("I've answered your questions:");
        formattedAnswers.AppendLine();

        foreach (var question in pendingQuestion.Questions)
        {
            formattedAnswers.AppendLine($"**{question.Header}**: {question.Question}");
            if (answers.TryGetValue(question.Question, out var answer))
            {
                formattedAnswers.AppendLine($"My answer: {answer}");
            }
            else
            {
                formattedAnswers.AppendLine("My answer: (no answer provided)");
            }
            formattedAnswers.AppendLine();
        }

        formattedAnswers.AppendLine("Please continue with the task based on my answers above.");

        await _messageProcessing.Value.SendMessageAsync(sessionId, formattedAnswers.ToString().Trim(), SessionMode.Build, cancellationToken);
    }

    public async Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null || string.IsNullOrEmpty(session.PlanContent))
        {
            _logger.LogWarning("Cannot execute plan: session {SessionId} not found or no plan content", sessionId);
            return;
        }

        _logger.LogInformation("Executing plan for session {SessionId}, clearContext={ClearContext}", sessionId, clearContext);

        if (clearContext)
        {
            await _lifecycle.Value.ClearContextAsync(sessionId, cancellationToken);
            var contextClearedEvent = AGUIEvents.AGUIEventFactory.CreateCustomEvent(AGUICustomEventName.ContextCleared, sessionId);
            await _hubContext.BroadcastAGUICustomEvent(sessionId, contextClearedEvent);
        }

        session.Status = ClaudeSessionStatus.Running;
        session.HasPendingPlanApproval = false;
        session.PlanHasBeenApproved = true;
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, hasPendingPlanApproval: false);

        // Re-read plan from disk to ensure we have the latest content
        var planContent = session.PlanContent;
        if (!string.IsNullOrEmpty(session.PlanFilePath))
        {
            var (_, freshContent) = await TryReadPlanFileAsync(session.WorkingDirectory, session.PlanFilePath);
            if (!string.IsNullOrEmpty(freshContent))
            {
                planContent = freshContent;
            }
        }

        // Clear plan state after capturing content
        ClearPlanState(session);

        var executionMessage = $"Please proceed with the implementation of the plan below. The full plan is provided here — do NOT attempt to read or find a plan file on disk.\n\n{planContent}";
        await _messageProcessing.Value.SendMessageAsync(sessionId, executionMessage, SessionMode.Build, cancellationToken);
    }

    public async Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        var canApprovePlan = session.Status == ClaudeSessionStatus.WaitingForPlanExecution ||
                             (session.Status == ClaudeSessionStatus.WaitingForInput && session.HasPendingPlanApproval);
        if (!canApprovePlan)
        {
            throw new InvalidOperationException($"Session {sessionId} is not waiting for plan approval (status: {session.Status})");
        }

        _logger.LogInformation(
            "Plan approval for session {SessionId}: approved={Approved}, keepContext={KeepContext}, hasFeedback={HasFeedback}",
            sessionId, approved, keepContext, feedback != null);

        if (approved)
        {
            // Clear the pending plan approval flag immediately (UI should hide plan controls)
            // Note: PlanContent/PlanFilePath are preserved for ExecutePlanAsync to use
            session.HasPendingPlanApproval = false;
            session.PlanHasBeenApproved = true;

            if (keepContext)
            {
                session.Status = ClaudeSessionStatus.Running;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, hasPendingPlanApproval: false);

                var keepCtxAgentId = _stateManager.GetAgentSessionId(sessionId);
                if (keepCtxAgentId != null)
                {
                    var resolved = await _agentExecutionService.ApprovePlanAsync(
                        keepCtxAgentId, true, true, null, cancellationToken);
                    if (resolved)
                    {
                        _logger.LogInformation("ApprovePlanAsync: Worker approved plan (keep context) for session {SessionId}", sessionId);
                        await AppendInteractiveToolResultAsync(session,
                            new { approved = true, keepContext = true, feedback = (string?)null }, cancellationToken);
                        return;
                    }
                }

                await ExecutePlanAsync(sessionId, clearContext: false, cancellationToken);
            }
            else
            {
                session.Status = ClaudeSessionStatus.Running;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, hasPendingPlanApproval: false);

                var clearCtxAgentId = _stateManager.GetAgentSessionId(sessionId);
                if (clearCtxAgentId != null)
                {
                    var resolved = await _agentExecutionService.ApprovePlanAsync(
                        clearCtxAgentId, true, false, null, cancellationToken);
                    if (resolved)
                    {
                        _logger.LogInformation("ApprovePlanAsync: Worker notified (clear context) for session {SessionId}", sessionId);
                        await AppendInteractiveToolResultAsync(session,
                            new { approved = true, keepContext = false, feedback = (string?)null }, cancellationToken);
                    }
                }

                await ExecutePlanAsync(sessionId, clearContext: true, cancellationToken);
            }
        }
        else
        {
            // Reject: clear plan state so new plan_pending events aren't blocked by the guard
            // and stale PlanContent isn't used as a fallback
            session.HasPendingPlanApproval = false;
            session.PlanHasBeenApproved = false;
            session.PlanContent = null;
            session.PlanFilePath = null;

            session.Status = ClaudeSessionStatus.Running;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status, hasPendingPlanApproval: false);

            var rejectAgentId = _stateManager.GetAgentSessionId(sessionId);
            if (rejectAgentId != null)
            {
                var resolved = await _agentExecutionService.ApprovePlanAsync(
                    rejectAgentId, false, false, feedback, cancellationToken);
                if (resolved)
                {
                    _logger.LogInformation("ApprovePlanAsync: Worker rejected plan for session {SessionId}", sessionId);
                    await AppendInteractiveToolResultAsync(session,
                        new { approved = false, keepContext = false, feedback }, cancellationToken);
                    return;
                }
            }

            var rejectMessage = !string.IsNullOrEmpty(feedback)
                ? $"I've reviewed your plan and would like changes. Here's my feedback:\n\n{feedback}\n\nPlease revise the plan based on my feedback."
                : "I've reviewed your plan and would like you to revise it. Please create an updated plan.";

            await _messageProcessing.Value.SendMessageAsync(sessionId, rejectMessage, SessionMode.Build, cancellationToken);
        }
    }

    public void TryCaptureWrittenPlanContent(ClaudeSession session, string? toolInputJson)
    {
        if (string.IsNullOrEmpty(toolInputJson))
            return;

        try
        {
            var inputParams = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInputJson);
            if (inputParams == null)
                return;

            if (!inputParams.TryGetValue("file_path", out var filePathElement) ||
                filePathElement.ValueKind != JsonValueKind.String)
                return;

            var filePath = filePathElement.GetString();
            if (string.IsNullOrEmpty(filePath))
                return;

            var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();
            var isPlanFile = normalizedPath.Contains("/plans/") ||
                             (normalizedPath.Contains("/.claude/") && normalizedPath.EndsWith("plan.md"));

            if (!isPlanFile)
                return;

            if (!inputParams.TryGetValue("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
                return;

            var content = contentElement.GetString();
            if (string.IsNullOrEmpty(content))
                return;

            session.PlanContent = content;
            session.PlanFilePath = filePath;
            _logger.LogInformation("Captured plan content from Write tool: {FilePath} ({Length} chars)",
                filePath, content.Length);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Write tool input for plan content capture");
        }
    }

    // --- Private helpers ---

    /// <summary>
    /// After the worker confirms an ask_user_question answer or a propose_plan approval, the
    /// matching interactive tool call must transition from requires-action to complete by
    /// emitting a TOOL_CALL_RESULT on the session's event stream. The toolCallId was assigned
    /// by <see cref="A2AToAGUITranslator.BuildInputRequired"/> and cached in
    /// <see cref="IPendingToolCallRegistry"/>; we dequeue it here and feed a synthetic
    /// tool_result A2A user message through <see cref="IToolCallResultAppender"/> so live +
    /// replay see the identical completion.
    /// </summary>
    private async Task AppendInteractiveToolResultAsync(
        ClaudeSession session,
        object resultPayload,
        CancellationToken cancellationToken)
    {
        var toolCallId = _pendingToolCalls.Dequeue(session.Id);
        if (toolCallId is null)
        {
            _logger.LogWarning(
                "AppendInteractiveToolResultAsync: no pending toolCallId registered for session {SessionId} — client probably double-submitted or the server was restarted between start and result",
                session.Id);
            return;
        }

        await _toolCallResultAppender.AppendAsync(
            session.ProjectId,
            session.Id,
            toolCallId,
            resultPayload,
            cancellationToken);
    }

    private static List<UserQuestion> ParseQuestions(JsonElement questionsElement)
    {
        var questions = new List<UserQuestion>();
        foreach (var questionElement in questionsElement.EnumerateArray())
        {
            var options = new List<QuestionOption>();
            if (questionElement.TryGetProperty("options", out var optionsElement))
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    options.Add(new QuestionOption
                    {
                        Label = optionElement.GetProperty("label").GetString() ?? "",
                        Description = optionElement.GetProperty("description").GetString() ?? ""
                    });
                }
            }

            questions.Add(new UserQuestion
            {
                Question = questionElement.GetProperty("question").GetString() ?? "",
                Header = questionElement.TryGetProperty("header", out var headerElement) ? headerElement.GetString() ?? "" : "",
                Options = options,
                MultiSelect = questionElement.TryGetProperty("multiSelect", out var multiSelectElement) && multiSelectElement.GetBoolean()
            });
        }
        return questions;
    }

    private static void ClearPlanState(ClaudeSession session)
    {
        session.PlanContent = null;
        session.PlanFilePath = null;
        session.HasPendingPlanApproval = false;
        session.PlanHasBeenApproved = false;
    }

    private string? TryGetPlanFilePath(Dictionary<string, JsonElement>? inputParams, string workingDirectory)
    {
        if (inputParams == null) return null;

        string[] possibleKeys = ["planFile", "planFilePath", "file", "path"];
        foreach (var key in possibleKeys)
        {
            if (inputParams.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var path = value.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    return Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path);
                }
            }
        }

        return null;
    }

    private async Task<(string? foundPath, string? content)> TryReadPlanFileAsync(string workingDirectory, string? specifiedPath)
    {
        var pathsToTry = new List<string>();

        if (!string.IsNullOrEmpty(specifiedPath))
            pathsToTry.Add(specifiedPath);

        pathsToTry.Add(Path.Combine(workingDirectory, "PLAN.md"));
        pathsToTry.Add(Path.Combine(workingDirectory, ".claude", "plan.md"));
        pathsToTry.Add(Path.Combine(workingDirectory, ".claude", "PLAN.md"));

        foreach (var path in pathsToTry)
        {
            if (File.Exists(path))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(path);
                    _logger.LogDebug("Successfully read plan file from {Path}", path);
                    return (path, content);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read plan file at {Path}", path);
                }
            }
        }

        return (null, null);
    }

    private async Task<string?> TryReadPlanFromAgentAsync(
        string agentSessionId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var pathsToTry = new[]
        {
            Path.Combine(workingDirectory, "PLAN.md"),
            Path.Combine(workingDirectory, ".claude", "plan.md"),
            Path.Combine(workingDirectory, ".claude", "PLAN.md")
        };

        foreach (var path in pathsToTry)
        {
            var content = await _agentExecutionService.ReadFileFromAgentAsync(agentSessionId, path, cancellationToken);
            if (!string.IsNullOrEmpty(content))
            {
                _logger.LogDebug("Found plan file via agent at {Path}", path);
                return content;
            }
        }

        return null;
    }
}
