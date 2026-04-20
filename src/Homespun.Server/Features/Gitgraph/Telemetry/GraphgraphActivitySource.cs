using System.Diagnostics;

namespace Homespun.Features.Gitgraph.Telemetry;

/// <summary>
/// Singleton ActivitySource used by Gitgraph services for task-graph spans.
/// Registered on the OTLP tracer provider in <c>Program.cs</c> and
/// <c>MockServiceExtensions.AddMockServices</c>.
/// </summary>
public static class GraphgraphActivitySource
{
    public const string Name = "Homespun.Gitgraph";

    public static readonly ActivitySource Instance = new(Name);
}
