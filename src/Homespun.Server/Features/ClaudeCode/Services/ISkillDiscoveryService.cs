using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Scans <c>.claude/skills/**/SKILL.md</c> under a project's active clone and
/// returns categorised skill descriptors for UI surfacing and session dispatch.
/// </summary>
public interface ISkillDiscoveryService
{
    /// <summary>
    /// Scan the given project path for skill definitions.
    /// </summary>
    /// <param name="projectPath">
    /// The filesystem root of the clone (the directory containing
    /// <c>.claude/skills/</c>). Returns empty collections if the directory
    /// is absent.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DiscoveredSkills"/> grouping of OpenSpec / Homespun /
    /// general skills.
    /// </returns>
    Task<DiscoveredSkills> DiscoverSkillsAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load a single skill by name from the given project path.
    /// Used by the dispatcher to resolve a skill body at session-start time
    /// without scanning every skill in the directory.
    /// </summary>
    /// <param name="projectPath">The clone root.</param>
    /// <param name="skillName">The skill directory name (and frontmatter <c>name</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The descriptor including <c>SkillBody</c>, or null if not found.</returns>
    Task<SkillDescriptor?> GetSkillAsync(
        string projectPath,
        string skillName,
        CancellationToken cancellationToken = default);
}
