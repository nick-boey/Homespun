namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Result of a mini-prompt execution.
/// </summary>
/// <param name="Success">Whether the prompt executed successfully</param>
/// <param name="Response">The AI response text if successful</param>
/// <param name="Error">Error message if failed</param>
/// <param name="CostUsd">Total cost of the request in USD</param>
/// <param name="DurationMs">Duration of the request in milliseconds</param>
public record MiniPromptResult(
    bool Success,
    string? Response,
    string? Error,
    decimal? CostUsd,
    int? DurationMs);

/// <summary>
/// Service for executing lightweight AI prompts without tools.
/// Used for simple text generation tasks like branch ID generation,
/// title suggestions, etc.
/// </summary>
public interface IMiniPromptService
{
    /// <summary>
    /// Executes a lightweight AI prompt and returns the response.
    /// </summary>
    /// <param name="prompt">The prompt to send</param>
    /// <param name="model">The model to use (default: haiku)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI response result</returns>
    Task<MiniPromptResult> ExecuteAsync(
        string prompt,
        string model = "haiku",
        CancellationToken cancellationToken = default);
}
