namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Represents a worker container discovered via Docker labels.
/// Contains metadata needed to re-register the container after server restart.
/// </summary>
public record DiscoveredContainer(
    string ContainerId,
    string ContainerName,
    string WorkerUrl,
    string? ProjectId,
    string? IssueId,
    string WorkingDirectory,
    DateTime CreatedAt);

/// <summary>
/// Service for discovering Homespun worker containers after server restart.
/// Uses Docker labels to identify containers that were managed by this server.
/// </summary>
public interface IContainerDiscoveryService
{
    /// <summary>
    /// Discovers all running Homespun worker containers using Docker labels.
    /// Only returns healthy containers that respond to health checks.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of discovered containers with their metadata.</returns>
    Task<IReadOnlyList<DiscoveredContainer>> DiscoverHomespunContainersAsync(CancellationToken ct);
}
