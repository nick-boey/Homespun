using System.Diagnostics;

namespace Homespun.Features.Commands.Telemetry;

/// <summary>
/// Singleton ActivitySource wrapping <see cref="CommandRunner"/> subprocess
/// invocations as <c>cmd.run</c> spans.
/// </summary>
public static class CommandsActivitySource
{
    public const string Name = "Homespun.Commands";

    public static readonly ActivitySource Instance = new(Name);
}
