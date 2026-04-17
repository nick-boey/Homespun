namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Categorises a discovered skill so the UI can surface it in the right place.
/// </summary>
public enum SkillCategory
{
    /// <summary>
    /// A hard-coded OpenSpec skill (e.g. openspec-explore, openspec-apply-change).
    /// Surfaced in the OpenSpec agent-runner tab.
    /// </summary>
    OpenSpec,

    /// <summary>
    /// A user-authored Homespun prompt skill identified by <c>homespun: true</c> in
    /// SKILL.md frontmatter. Surfaced in the Task Agent tab as a selectable prompt.
    /// </summary>
    Homespun,

    /// <summary>
    /// Any other skill present in <c>.claude/skills/</c>. Surfaced in the session
    /// chat window for Claude to auto-invoke during free-form conversation.
    /// </summary>
    General
}
