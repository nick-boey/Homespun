using Homespun.ClaudeAgentSdk;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Service for executing lightweight AI prompts without tools.
/// Uses Claude Agent SDK configured for minimal, tool-free operation.
/// </summary>
public class MiniPromptService : IMiniPromptService
{
    private readonly ILogger<MiniPromptService> _logger;

    public MiniPromptService(ILogger<MiniPromptService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MiniPromptService>.Instance;
    }

    /// <inheritdoc />
    public async Task<MiniPromptResult> ExecuteAsync(
        string prompt,
        string model = "haiku",
        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
        }

        // Check cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();

        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Executing mini-prompt with model {Model}: {PromptPreview}",
                model, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

            var options = new ClaudeCodeChatClientOptions
            {
                Model = model,
                PermissionMode = PermissionMode.BypassPermissions,
                AllowedTools = new List<string>(), // No tools - pure text generation
                MaxTurns = 1 // Single response only
            };

            await using var client = new ClaudeCodeChatClient(options);

            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.User, prompt)
            };

            var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);

            // ChatResponse has a Messages collection - get the text from the first/last message
            var responseText = response.Messages?.LastOrDefault()?.Text;
            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Extract cost from response metadata if available
            decimal? costUsd = null;
            if (response.AdditionalProperties?.TryGetValue("TotalCostUsd", out var costObj) == true)
            {
                if (costObj is decimal d)
                    costUsd = d;
                else if (costObj is double dbl)
                    costUsd = (decimal)dbl;
            }

            _logger.LogDebug("Mini-prompt completed in {DurationMs}ms, cost: ${CostUsd}",
                durationMs, costUsd?.ToString("F6") ?? "N/A");

            return new MiniPromptResult(
                Success: !string.IsNullOrEmpty(responseText),
                Response: responseText,
                Error: string.IsNullOrEmpty(responseText) ? "Empty response from AI" : null,
                CostUsd: costUsd,
                DurationMs: durationMs);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Mini-prompt cancelled");
            throw;
        }
        catch (Exception ex)
        {
            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogWarning(ex, "Mini-prompt failed after {DurationMs}ms: {Error}", durationMs, ex.Message);

            return new MiniPromptResult(
                Success: false,
                Response: null,
                Error: ex.Message,
                CostUsd: null,
                DurationMs: durationMs);
        }
    }
}
