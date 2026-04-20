using System.Diagnostics;

namespace Homespun.Features.OpenSpec.Telemetry;

/// <summary>
/// Singleton ActivitySource used by OpenSpec enrichment services for
/// enrich / resolve / scan / reconcile spans.
/// </summary>
public static class OpenSpecActivitySource
{
    public const string Name = "Homespun.OpenSpec";

    public static readonly ActivitySource Instance = new(Name);
}
