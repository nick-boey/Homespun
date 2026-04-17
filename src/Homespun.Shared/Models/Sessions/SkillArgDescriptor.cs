namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Describes a single argument expected by a Homespun skill so the UI can render
/// the right control for it.
/// </summary>
public class SkillArgDescriptor
{
    /// <summary>
    /// The argument name (matches the frontmatter <c>homespun-args[].name</c>).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The kind of input control to render.
    /// </summary>
    public SkillArgKind Kind { get; set; } = SkillArgKind.FreeText;

    /// <summary>
    /// Optional human-readable label; falls back to <see cref="Name"/> if absent.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Optional description shown alongside the input.
    /// </summary>
    public string? Description { get; set; }
}
