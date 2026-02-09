namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a project.
/// </summary>
public class CreateProjectRequest
{
    /// <summary>
    /// GitHub owner/repository (e.g., "owner/repo") for cloning from GitHub.
    /// </summary>
    public string? OwnerRepo { get; set; }

    /// <summary>
    /// Project name for creating a local-only project.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Default branch name (defaults to "main").
    /// </summary>
    public string? DefaultBranch { get; set; }
}

/// <summary>
/// Request model for updating a project.
/// </summary>
public class UpdateProjectRequest
{
    /// <summary>
    /// Default model for agent sessions.
    /// </summary>
    public string? DefaultModel { get; set; }
}
