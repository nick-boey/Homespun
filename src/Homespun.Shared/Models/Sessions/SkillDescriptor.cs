namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// A skill discovered under <c>.claude/skills/</c>. The descriptor carries the
/// metadata needed to display the skill in the UI and, for
/// <see cref="SkillCategory.Homespun"/> and <see cref="SkillCategory.OpenSpec"/>
/// skills, to dispatch a session using the skill body as the initial message.
/// </summary>
public class SkillDescriptor
{
    /// <summary>
    /// The skill directory name (and frontmatter <c>name</c>).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The short description from the skill's frontmatter <c>description</c>.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How this skill is categorised for UI surfacing.
    /// </summary>
    public SkillCategory Category { get; set; }

    /// <summary>
    /// The full SKILL.md body (content below the frontmatter). Used as the
    /// starting text when dispatching a session. Null when only metadata was
    /// read (e.g. when serialising over the API).
    /// </summary>
    public string? SkillBody { get; set; }

    /// <summary>
    /// For Homespun skills: the session mode declared via
    /// <c>homespun-mode: plan|build</c>.
    /// </summary>
    public SessionMode? Mode { get; set; }

    /// <summary>
    /// For Homespun skills: the declared arguments from <c>homespun-args</c>.
    /// Empty for skills that declare no args.
    /// </summary>
    public IReadOnlyList<SkillArgDescriptor> Args { get; set; } = Array.Empty<SkillArgDescriptor>();
}
