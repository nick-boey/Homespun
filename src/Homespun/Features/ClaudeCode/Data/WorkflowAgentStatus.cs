namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents the status of an agent in the workflow.
/// </summary>
public class WorkflowAgentStatus
{
    /// <summary>
    /// The entity ID the agent is associated with.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// The session information.
    /// </summary>
    public required ClaudeSession Session { get; init; }

    /// <summary>
    /// The type of harness being used (always "ClaudeAgentSDK").
    /// </summary>
    public string HarnessType => "ClaudeAgentSDK";

    /// <summary>
    /// Whether the agent is currently running.
    /// </summary>
    public bool IsRunning => Session.Status == ClaudeSessionStatus.Running ||
                             Session.Status == ClaudeSessionStatus.Processing ||
                             Session.Status == ClaudeSessionStatus.WaitingForInput;
}
