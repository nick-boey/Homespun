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

    /// <summary>
    /// Host-filesystem directory bind-mounted into the worker container at
    /// <see cref="ContainerWorkspaceRoot"/>. Applies on <b>Windows hosts only</b>
    /// — the Linux worker container can't resolve a Windows <c>C:\...</c> cwd,
    /// so the shim rewrites that prefix to the container path before forwarding
    /// the request. On Linux / macOS the host path already resolves inside the
    /// container (via Docker Desktop's shared filesystem or matching paths) so
    /// this option is ignored even if set.
    /// </summary>
    public string HostWorkspaceRoot { get; set; } = string.Empty;

    /// <summary>
    /// Container-side mount point that <see cref="HostWorkspaceRoot"/> is
    /// bind-mounted to. Must match the <c>worker.volumes</c> target in
    /// <c>docker-compose.override.yml</c>. Default <c>/workdir</c>. Windows-only
    /// (see <see cref="HostWorkspaceRoot"/>).
    /// </summary>
    public string ContainerWorkspaceRoot { get; set; } = "/workdir";
}
