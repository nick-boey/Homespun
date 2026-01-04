using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Projects;

/// <summary>
/// Result of a project creation attempt.
/// </summary>
public class CreateProjectResult
{
    public bool Success { get; init; }
    public Project? Project { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateProjectResult Ok(Project project) => new() { Success = true, Project = project };
    public static CreateProjectResult Error(string message) => new() { Success = false, ErrorMessage = message };
}

public class ProjectService(
    IDataStore dataStore,
    IGitHubService gitHubService,
    ICommandRunner commandRunner)
{
    /// <summary>
    /// Base path for all project worktrees.
    /// </summary>
    private static string HomespunBasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun", "src");

    public Task<List<Project>> GetAllAsync()
    {
        var projects = dataStore.Projects
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();
        return Task.FromResult(projects);
    }

    public Task<Project?> GetByIdAsync(string id)
    {
        return Task.FromResult(dataStore.GetProject(id));
    }

    /// <summary>
    /// Creates a new project from a GitHub repository.
    /// </summary>
    /// <param name="ownerRepo">GitHub owner and repository in "owner/repo" format</param>
    /// <returns>Result containing the created project or an error message</returns>
    public async Task<CreateProjectResult> CreateAsync(string ownerRepo)
    {
        // Parse owner/repo
        var parts = ownerRepo.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return CreateProjectResult.Error("Invalid format. Expected 'owner/repository'.");
        }

        var owner = parts[0].Trim();
        var repo = parts[1].Trim();

        // Get default branch from GitHub
        var defaultBranch = await gitHubService.GetDefaultBranchAsync(owner, repo);
        if (defaultBranch == null)
        {
            return CreateProjectResult.Error($"Could not fetch repository '{owner}/{repo}' from GitHub. Check that the repository exists and GITHUB_TOKEN is configured.");
        }

        // Calculate local path: ~/.homespun/src/<repo>/<branch>
        var repoPath = Path.Combine(HomespunBasePath, repo);
        var localPath = Path.Combine(repoPath, defaultBranch);

        // Create directory structure if it doesn't exist
        Directory.CreateDirectory(repoPath);

        // Clone the repository to the local path
        var cloneUrl = $"https://github.com/{owner}/{repo}.git";
        var cloneResult = await commandRunner.RunAsync("git", $"clone \"{cloneUrl}\" \"{localPath}\"", repoPath);

        if (!cloneResult.Success)
        {
            // Check if it already exists
            if (Directory.Exists(localPath) && Directory.Exists(Path.Combine(localPath, ".git")))
            {
                // Already cloned, continue
            }
            else
            {
                return CreateProjectResult.Error($"Failed to clone repository: {cloneResult.Error}");
            }
        }

        var project = new Project
        {
            Name = repo,
            LocalPath = localPath,
            GitHubOwner = owner,
            GitHubRepo = repo,
            DefaultBranch = defaultBranch
        };

        await dataStore.AddProjectAsync(project);
        return CreateProjectResult.Ok(project);
    }

    public async Task<Project?> UpdateAsync(
        string id,
        string? defaultModel = null)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return null;

        project.DefaultModel = defaultModel;
        project.UpdatedAt = DateTime.UtcNow;

        await dataStore.UpdateProjectAsync(project);
        return project;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var project = dataStore.GetProject(id);
        if (project == null) return false;

        await dataStore.RemoveProjectAsync(id);
        return true;
    }
}
