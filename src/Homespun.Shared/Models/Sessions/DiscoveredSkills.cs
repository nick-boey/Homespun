namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Result of a skills scan: the three categorised collections of skills
/// discovered under <c>.claude/skills/</c>.
/// </summary>
public class DiscoveredSkills
{
    /// <summary>
    /// Hard-coded OpenSpec skills present in the clone.
    /// </summary>
    public IReadOnlyList<SkillDescriptor> OpenSpec { get; set; } = Array.Empty<SkillDescriptor>();

    /// <summary>
    /// User-authored Homespun skills (<c>homespun: true</c> in frontmatter).
    /// </summary>
    public IReadOnlyList<SkillDescriptor> Homespun { get; set; } = Array.Empty<SkillDescriptor>();

    /// <summary>
    /// All other skills — surfaced in the session chat window.
    /// </summary>
    public IReadOnlyList<SkillDescriptor> General { get; set; } = Array.Empty<SkillDescriptor>();
}
