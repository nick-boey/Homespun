namespace Homespun.Shared.Models.Design;

/// <summary>
/// Metadata for a design system component.
/// </summary>
public class ComponentMetadata
{
    /// <summary>
    /// The unique identifier/slug for the component (used in URL).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the component.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Brief description of the component's purpose.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Category for grouping components (e.g., "Core", "Chat", "Forms").
    /// </summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// Relative path to the component file from Components folder.
    /// </summary>
    public string? ComponentPath { get; init; }

    /// <summary>
    /// Tags for filtering/searching components.
    /// </summary>
    public List<string> Tags { get; init; } = new();
}
