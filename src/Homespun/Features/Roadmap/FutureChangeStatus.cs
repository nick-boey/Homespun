namespace Homespun.Features.Roadmap;

/// <summary>
/// Status of a future change in the roadmap.
/// </summary>
public enum FutureChangeStatus
{
    /// <summary>
    /// Change is planned but not yet started.
    /// </summary>
    Pending,

    /// <summary>
    /// Agent is currently working on the change.
    /// </summary>
    InProgress,

    /// <summary>
    /// Agent completed work but PR creation is pending or failed.
    /// </summary>
    AwaitingPR,

    /// <summary>
    /// PR has been created. Change can be removed from roadmap.
    /// </summary>
    Complete
}
