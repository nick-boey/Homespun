namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents the current status of a Claude Code session.
/// </summary>
public enum ClaudeSessionStatus
{
    /// <summary>
    /// Session is starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Session is executing startup hooks.
    /// </summary>
    RunningHooks,

    /// <summary>
    /// Agent is generating responses.
    /// </summary>
    Running,

    /// <summary>
    /// Session is waiting for user input.
    /// </summary>
    WaitingForInput,

    /// <summary>
    /// Session is waiting for the user to answer a question from Claude.
    /// </summary>
    WaitingForQuestionAnswer,

    /// <summary>
    /// Session is waiting for the user to execute a plan.
    /// </summary>
    WaitingForPlanExecution,

    /// <summary>
    /// Session has stopped normally.
    /// </summary>
    Stopped,

    /// <summary>
    /// Session encountered an error.
    /// </summary>
    Error
}
