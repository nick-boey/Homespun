using Homespun.Features.Containers.Services;
using Homespun.Shared.Models.Containers;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IContainerQueryService that returns empty data.
/// </summary>
public class MockContainerQueryService : IContainerQueryService
{
    private readonly ILogger<MockContainerQueryService> _logger;

    public MockContainerQueryService(ILogger<MockContainerQueryService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<WorkerContainerDto>> GetAllContainersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetAllContainersAsync - returning empty list");
        return Task.FromResult<IReadOnlyList<WorkerContainerDto>>(Array.Empty<WorkerContainerDto>());
    }

    public Task<bool> StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopContainerAsync {ContainerId} - returning false", containerId);
        return Task.FromResult(false);
    }
}
