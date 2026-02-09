
namespace Homespun.Features.Projects;

/// <summary>
/// Interface for project management operations.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Gets all projects.
    /// </summary>
    Task<List<Project>> GetAllAsync();

    /// <summary>
    /// Gets a project by ID.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <returns>The project, or null if not found.</returns>
    Task<Project?> GetByIdAsync(string id);

    /// <summary>
    /// Creates a new local project with a fresh git repository.
    /// </summary>
    /// <param name="name">Project name (used for folder name).</param>
    /// <param name="defaultBranch">Default branch name (defaults to "main").</param>
    Task<CreateProjectResult> CreateLocalAsync(string name, string defaultBranch = "main");

    /// <summary>
    /// Creates a new project from a GitHub repository.
    /// </summary>
    /// <param name="ownerRepo">GitHub owner and repository in "owner/repo" format.</param>
    Task<CreateProjectResult> CreateAsync(string ownerRepo);

    /// <summary>
    /// Updates project settings.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="defaultModel">Optional default model.</param>
    Task<Project?> UpdateAsync(string id, string? defaultModel = null);

    /// <summary>
    /// Deletes a project.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string id);
}
