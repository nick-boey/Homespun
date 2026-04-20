using System.Diagnostics;

namespace Homespun.Features.Observability;

/// <summary>
/// Custom ActivitySources for tracing key Homespun operations.
/// Register these with OpenTelemetry via <see cref="HomespunTelemetryExtensions.AddHomespunInstrumentation"/>.
/// </summary>
public static class HomespunActivitySources
{
    public const string AgentOrchestration = "Homespun.AgentOrchestration";
    public const string GitClone = "Homespun.GitClone";
    public const string FleeceSync = "Homespun.FleeceSync";
    public const string Signalr = "Homespun.Signalr";
    public const string SessionPipeline = "Homespun.SessionPipeline";

    public static readonly ActivitySource AgentOrchestrationSource = new(AgentOrchestration);
    public static readonly ActivitySource GitCloneSource = new(GitClone);
    public static readonly ActivitySource FleeceSyncSource = new(FleeceSync);
    public static readonly ActivitySource SessionPipelineSource = new(SessionPipeline);
    public static readonly ActivitySource SignalrSource = new(Signalr);

    internal static readonly string[] AllSourceNames =
    [
        AgentOrchestration,
        GitClone,
        FleeceSync,
        Signalr,
        SessionPipeline,
    ];
}
