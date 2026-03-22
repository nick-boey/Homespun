namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Defines the type of Claude Code session.
/// </summary>
public enum SessionType
{
    /// <summary>
    /// Standard session - normal coding tasks.
    /// </summary>
    Standard,

    /// <summary>
    /// Issue agent modification session - provides the initial user message template for issue agent sessions.
    /// </summary>
    IssueAgentModification,

    /// <summary>
    /// Issue agent system prompt - provides constraints and guidance for issue agent sessions.
    /// </summary>
    IssueAgentSystem
}