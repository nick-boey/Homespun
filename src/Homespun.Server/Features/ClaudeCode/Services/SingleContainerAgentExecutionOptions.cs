namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Configuration for <c>SingleContainerAgentExecutionService</c>, the
/// dev-only shim that forwards every agent session to a pre-running
/// <c>homespun-worker</c> docker-compose container. Bound from the
/// <c>AgentExecution:SingleContainer</c> configuration section.
/// </summary>
public sealed class SingleContainerAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:SingleContainer";

    /// <summary>
    /// URL of the pre-running worker container, for example
    /// <c>http://localhost:8081</c>. Required — startup fails fast when missing.
    /// </summary>
    public string WorkerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum time the shim waits for a single worker HTTP call to complete
    /// (including long-lived SSE streams). Default matches
    /// <c>DockerAgentExecutionOptions.RequestTimeout</c>.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
