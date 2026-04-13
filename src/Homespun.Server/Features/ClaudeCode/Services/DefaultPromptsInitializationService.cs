namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Ensures default agent prompts exist when the application starts.
/// </summary>
public class DefaultPromptsInitializationService : IHostedService
{
    private readonly IAgentPromptService _agentPromptService;
    private readonly ILogger<DefaultPromptsInitializationService> _logger;

    public DefaultPromptsInitializationService(
        IAgentPromptService agentPromptService,
        ILogger<DefaultPromptsInitializationService> logger)
    {
        _agentPromptService = agentPromptService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            _logger.LogInformation("Initializing default agent prompts...");
            await _agentPromptService.EnsureDefaultPromptsAsync();
            _logger.LogInformation("Default agent prompts initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize default agent prompts");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
