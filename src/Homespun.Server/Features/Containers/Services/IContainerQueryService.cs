using Homespun.Shared.Models.Containers;

namespace Homespun.Features.Containers.Services;

/// <summary>
/// Service for querying and managing worker containers.
/// </summary>
public interface IContainerQueryService
{
    /// <summary>
    /// Gets all worker containers currently tracked by the application.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of worker container DTOs with enriched project/issue information.</returns>
    Task<IReadOnlyList<WorkerContainerDto>> GetAllContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a container by its ID.
    /// </summary>
    /// <param name="containerId">The container ID to stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the container was found and stopped, false if not found.</returns>
    Task<bool> StopContainerAsync(string containerId, CancellationToken cancellationToken = default);
}
