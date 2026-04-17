namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// The kind of UI control that should be rendered for a skill argument.
/// Declared in a Homespun skill's SKILL.md frontmatter via <c>homespun-args[].kind</c>.
/// </summary>
public enum SkillArgKind
{
    /// <summary>
    /// Free-text input — default when no kind is specified.
    /// </summary>
    FreeText,

    /// <summary>
    /// A Fleece issue picker.
    /// </summary>
    Issue,

    /// <summary>
    /// A change picker populated from the project's changes.
    /// </summary>
    Change,

    /// <summary>
    /// A multi-select of phases parsed from the linked change's tasks.md.
    /// </summary>
    PhaseList
}
