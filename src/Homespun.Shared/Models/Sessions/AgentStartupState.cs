namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Represents the startup state of an agent.
/// </summary>
public class AgentStartupState
{
    public required string EntityId { get; init; }
    public AgentStartupStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Status of agent startup.
/// </summary>
public enum AgentStartupStatus
{
    Starting,
    Started,
    Failed
}
