namespace Homespun.Shared.Models.Projects;

public class CreateProjectResult
{
    public bool Success { get; init; }
    public Project? Project { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateProjectResult Ok(Project project) => new() { Success = true, Project = project };
    public static CreateProjectResult Error(string message) => new() { Success = false, ErrorMessage = message };
}
