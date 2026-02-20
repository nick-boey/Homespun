using Homespun.Shared.Models.Plans;

namespace Homespun.Features.Plans;

/// <summary>
/// Service for listing and reading plan files from Claude Code sessions.
/// </summary>
public interface IPlansService
{
    /// <summary>
    /// Lists all plan files in the .claude/plans directory of the working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of plan file information ordered by last modified date (newest first).</returns>
    Task<List<PlanFileInfo>> ListPlanFilesAsync(string workingDirectory, CancellationToken ct = default);

    /// <summary>
    /// Gets the content of a specific plan file.
    /// </summary>
    /// <param name="workingDirectory">The working directory containing the .claude/plans folder.</param>
    /// <param name="fileName">The name of the plan file (must not contain path separators).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The content of the plan file, or null if not found.</returns>
    /// <exception cref="SecurityException">Thrown if the fileName contains path traversal attempts.</exception>
    Task<string?> GetPlanContentAsync(string workingDirectory, string fileName, CancellationToken ct = default);
}
