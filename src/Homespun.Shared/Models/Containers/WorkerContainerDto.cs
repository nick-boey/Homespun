using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Models.Containers;

/// <summary>
/// Represents a worker container (Docker or Azure Container App) started by the application.
/// </summary>
public class WorkerContainerDto
{
    /// <summary>
    /// Unique identifier for the container (Docker container ID or ACA app name).
    /// </summary>
    public required string ContainerId { get; init; }

    /// <summary>
    /// Display name of the container.
    /// </summary>
    public required string ContainerName { get; init; }

    /// <summary>
    /// Working directory mounted in the container.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Project ID this container is associated with, if any.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Project name this container is associated with, if any.
    /// </summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// Issue ID this container was started for, if any.
    /// </summary>
    public string? IssueId { get; init; }

    /// <summary>
    /// Issue title this container was started for, if any.
    /// </summary>
    public string? IssueTitle { get; init; }

    /// <summary>
    /// Active session ID running in this container, if any.
    /// </summary>
    public string? ActiveSessionId { get; init; }

    /// <summary>
    /// Current status of the session in the container.
    /// </summary>
    public ClaudeSessionStatus SessionStatus { get; init; }

    /// <summary>
    /// Last activity timestamp of the session.
    /// </summary>
    public DateTime? LastActivityAt { get; init; }

    /// <summary>
    /// When the container was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Whether there is a pending question waiting for user input.
    /// </summary>
    public bool HasPendingQuestion { get; init; }

    /// <summary>
    /// Whether there is a pending plan approval waiting.
    /// </summary>
    public bool HasPendingPlanApproval { get; init; }
}
