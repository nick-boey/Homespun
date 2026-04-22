namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Configuration for the Fleece issue history ring buffer.
///
/// <para>
/// Bound from the <c>FleeceHistory</c> section of <c>appsettings.json</c>.
/// </para>
/// </summary>
public sealed class FleeceHistoryOptions
{
    public const string SectionName = "FleeceHistory";

    /// <summary>
    /// Maximum number of history entries to keep per project. Oldest entries are
    /// pruned when exceeded. Defaults to 100.
    /// </summary>
    public int MaxHistoryEntries { get; set; } = 100;
}
