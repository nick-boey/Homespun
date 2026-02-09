namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Tracks agent startup state for UI feedback.
/// </summary>
public interface IAgentStartupTracker
{
    /// <summary>
    /// Marks an entity as starting up.
    /// </summary>
    void MarkAsStarting(string entityId);

    /// <summary>
    /// Atomically attempts to mark an entity as starting up.
    /// Returns true if the entity was not already tracked (i.e., first caller wins).
    /// Returns false if the entity is already starting or started.
    /// </summary>
    bool TryMarkAsStarting(string entityId);

    /// <summary>
    /// Marks an entity as successfully started.
    /// </summary>
    void MarkAsStarted(string entityId);

    /// <summary>
    /// Marks an entity startup as failed.
    /// </summary>
    void MarkAsFailed(string entityId, string error);

    /// <summary>
    /// Clears the startup state for an entity.
    /// </summary>
    void Clear(string entityId);

    /// <summary>
    /// Checks if an entity is currently starting.
    /// </summary>
    bool IsStarting(string entityId);

    /// <summary>
    /// Gets the startup state for an entity.
    /// </summary>
    AgentStartupState? GetState(string entityId);

    /// <summary>
    /// Event raised when startup state changes.
    /// </summary>
    event Action<string, AgentStartupState>? OnStateChanged;
}

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
