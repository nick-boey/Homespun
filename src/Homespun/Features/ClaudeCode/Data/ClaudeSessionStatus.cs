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
    /// Agent is generating responses.
    /// </summary>
    Running,

    /// <summary>
    /// Session is waiting for user input.
    /// </summary>
    WaitingForInput,

    /// <summary>
    /// Session has stopped normally.
    /// </summary>
    Stopped,

    /// <summary>
    /// Session encountered an error.
    /// </summary>
    Error
}
