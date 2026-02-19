namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Specifies the agent execution mode.
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>
    /// Run agents in-process using the ClaudeAgentSdk directly.
    /// </summary>
    Local,

    /// <summary>
    /// Run agents in Docker containers using Docker-outside-of-Docker (DooD).
    /// </summary>
    Docker
}

/// <summary>
/// Configuration options for agent execution.
/// </summary>
public class AgentExecutionOptions
{
    public const string SectionName = "AgentExecution";

    /// <summary>
    /// The execution mode to use for running agents.
    /// </summary>
    public AgentExecutionMode Mode { get; set; } = AgentExecutionMode.Local;

    /// <summary>
    /// Maximum duration for an agent session.
    /// </summary>
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromMinutes(30);
}
