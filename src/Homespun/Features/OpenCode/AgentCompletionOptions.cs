namespace Homespun.Features.OpenCode;

/// <summary>
/// Configuration options for the agent completion monitor.
/// </summary>
public class AgentCompletionOptions
{
    /// <summary>
    /// Number of times to retry PR detection before failing.
    /// </summary>
    public int PrDetectionRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds between PR detection retries.
    /// </summary>
    public int PrDetectionRetryDelayMs { get; set; } = 5000;

    /// <summary>
    /// Overall timeout in milliseconds for PR detection.
    /// </summary>
    public int PrDetectionTimeoutMs { get; set; } = 60000;
}
