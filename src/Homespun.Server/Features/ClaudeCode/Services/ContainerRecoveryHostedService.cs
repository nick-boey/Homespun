using Microsoft.Extensions.Hosting;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Background service that discovers and re-registers Homespun worker containers
/// after a server restart. Runs once at startup to restore in-memory tracking state.
/// </summary>
public class ContainerRecoveryHostedService : BackgroundService
{
    private readonly IContainerDiscoveryService _discoveryService;
    private readonly Action<DiscoveredContainer> _registerContainer;
    private readonly ILogger<ContainerRecoveryHostedService> _logger;

    /// <summary>
    /// Creates a new instance of the container recovery service.
    /// </summary>
    /// <param name="discoveryService">Service for discovering containers.</param>
    /// <param name="registerContainer">Action to register a discovered container.</param>
    /// <param name="logger">Logger instance.</param>
    public ContainerRecoveryHostedService(
        IContainerDiscoveryService discoveryService,
        Action<DiscoveredContainer> registerContainer,
        ILogger<ContainerRecoveryHostedService> logger)
    {
        _discoveryService = discoveryService;
        _registerContainer = registerContainer;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let other services initialize
        try
        {
            await Task.Delay(1000, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("Starting container recovery...");

        try
        {
            var containers = await _discoveryService.DiscoverHomespunContainersAsync(stoppingToken);

            foreach (var container in containers)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    _registerContainer(container);
                    _logger.LogInformation(
                        "Recovered container {ContainerName} ({ContainerId}) for {WorkingDirectory}",
                        container.ContainerName, container.ContainerId, container.WorkingDirectory);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to register recovered container {ContainerName} ({ContainerId})",
                        container.ContainerName, container.ContainerId);
                }
            }

            _logger.LogInformation("Container recovery complete. Recovered {Count} containers.", containers.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Container recovery cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during container recovery");
        }
    }
}
