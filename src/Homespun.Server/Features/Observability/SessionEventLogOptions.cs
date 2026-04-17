namespace Homespun.Features.Observability;

/// <summary>
/// Configuration for <see cref="SessionEventLog"/> telemetry.
/// Bound from the <c>SessionEventLog</c> configuration section.
/// </summary>
public sealed class SessionEventLogOptions
{
    public const string SectionName = "SessionEventLog";

    /// <summary>
    /// Maximum number of characters to include in the <c>ContentPreview</c>
    /// field. Zero disables the field entirely. Default <c>0</c> in Production,
    /// <c>80</c> in Development (seeded via <c>appsettings.Development.json</c>).
    /// </summary>
    public int ContentPreviewChars { get; set; } = 0;

    /// <summary>
    /// Per-hop Enabled flags. An empty dictionary (default) means all hops are
    /// enabled. A hop name (see <c>SessionEventHops</c>) mapped to
    /// <c>{ "Enabled": false }</c> suppresses that hop's log entries.
    /// </summary>
    public Dictionary<string, HopSettings> Hops { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class HopSettings
    {
        public bool Enabled { get; set; } = true;
    }

    public bool IsHopEnabled(string hop)
    {
        if (Hops is null || Hops.Count == 0)
        {
            return true;
        }

        return !Hops.TryGetValue(hop, out var settings) || settings.Enabled;
    }
}
