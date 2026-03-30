using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles question/answer flow, plan management (capture, approval, execution),
/// workflow signals, and ExitPlanMode processing.
/// </summary>
public interface IToolInteractionService
{
    Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default);

    Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default);

    Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default);

    Task HandleWorkflowSignalToolAsync(string sessionId, ClaudeMessageContent toolUseContent, CancellationToken cancellationToken);

    Task HandleAskUserQuestionTool(string sessionId, ClaudeSession session, ClaudeMessageContent toolUseContent, CancellationToken cancellationToken);

    Task HandleQuestionPendingFromWorkerAsync(string sessionId, ClaudeSession session, SdkQuestionPendingMessage questionMsg, Guid turnId, CancellationToken cancellationToken);

    Task HandlePlanPendingFromWorkerAsync(string sessionId, ClaudeSession session, SdkPlanPendingMessage planMsg, Guid turnId, CancellationToken cancellationToken);

    Task HandleExitPlanModeCompletedAsync(string sessionId, ClaudeSession session, ClaudeMessageContent toolUseBlock, CancellationToken cancellationToken);

    void TryCaptureWrittenPlanContent(ClaudeSession session, ClaudeMessageContent toolUseBlock);

    void TryCaptureWrittenPlanContentFromResult(ClaudeSession session, ClaudeMessageContent toolResultBlock);
}
