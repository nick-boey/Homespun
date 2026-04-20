namespace Homespun.Features.Observability;

/// <summary>
/// Configuration for gating session-event content previews on spans.
/// Bound from the <c>SessionEventContent</c> configuration section. The legacy
/// <c>SessionEventLog</c> section is honoured as a fallback for one release —
/// see <c>Program.cs</c>.
/// </summary>
public sealed class SessionEventContentOptions
{
    public const string SectionName = "SessionEventContent";
    public const string LegacySectionName = "SessionEventLog";

    /// <summary>
    /// Maximum characters to include in the <c>homespun.content.preview</c>
    /// span attribute. Zero suppresses the attribute entirely. Default 0 in
    /// Production, 80 in Development (seeded via
    /// <c>appsettings.Development.json</c>).
    /// </summary>
    public int ContentPreviewChars { get; set; } = 0;

    /// <summary>
    /// Whether span emission is enabled at all. Defaults to true. Setting
    /// false is the emergency stop for the pipeline spans while keeping the
    /// session pipeline itself running.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
