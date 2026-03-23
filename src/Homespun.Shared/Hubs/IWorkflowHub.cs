namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines client-to-server SignalR messages for the Workflow hub.
/// </summary>
public interface IWorkflowHub
{
    /// <summary>
    /// Join an execution group to receive execution-specific events.
    /// </summary>
    Task JoinExecution(string executionId);

    /// <summary>
    /// Leave an execution group.
    /// </summary>
    Task LeaveExecution(string executionId);

    /// <summary>
    /// Join a project group to receive project-level workflow summary events.
    /// </summary>
    Task JoinProject(string projectId);

    /// <summary>
    /// Leave a project group.
    /// </summary>
    Task LeaveProject(string projectId);
}
