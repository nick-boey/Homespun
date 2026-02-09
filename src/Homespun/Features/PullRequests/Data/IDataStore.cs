
namespace Homespun.Features.PullRequests.Data;

/// <summary>
/// Interface for the JSON-based data store.
/// </summary>
public interface IDataStore
{
    #region Projects
    
    /// <summary>
    /// Gets all projects.
    /// </summary>
    IReadOnlyList<Project> Projects { get; }

    /// <summary>
    /// Adds a project to the store.
    /// </summary>
    Task AddProjectAsync(Project project);

    /// <summary>
    /// Updates a project in the store.
    /// </summary>
    Task UpdateProjectAsync(Project project);

    /// <summary>
    /// Removes a project from the store.
    /// </summary>
    Task RemoveProjectAsync(string projectId);

    /// <summary>
    /// Gets a project by ID.
    /// </summary>
    Project? GetProject(string id);
    
    #endregion

    #region Pull Requests
    
    /// <summary>
    /// Gets all pull requests.
    /// </summary>
    IReadOnlyList<PullRequest> PullRequests { get; }

    /// <summary>
    /// Adds a pull request to the store.
    /// </summary>
    Task AddPullRequestAsync(PullRequest pullRequest);

    /// <summary>
    /// Updates a pull request in the store.
    /// </summary>
    Task UpdatePullRequestAsync(PullRequest pullRequest);

    /// <summary>
    /// Removes a pull request from the store.
    /// </summary>
    Task RemovePullRequestAsync(string pullRequestId);

    /// <summary>
    /// Gets a pull request by ID.
    /// </summary>
    PullRequest? GetPullRequest(string id);

    /// <summary>
    /// Gets pull requests for a specific project.
    /// </summary>
    IReadOnlyList<PullRequest> GetPullRequestsByProject(string projectId);
    
    #endregion

    #region Favorite Models

    /// <summary>
    /// Gets all favorite model IDs.
    /// </summary>
    IReadOnlyList<string> FavoriteModels { get; }

    /// <summary>
    /// Adds a model to favorites.
    /// </summary>
    Task AddFavoriteModelAsync(string modelId);

    /// <summary>
    /// Removes a model from favorites.
    /// </summary>
    Task RemoveFavoriteModelAsync(string modelId);

    /// <summary>
    /// Checks if a model is in favorites.
    /// </summary>
    bool IsFavoriteModel(string modelId);

    #endregion

    #region Agent Prompts

    /// <summary>
    /// Gets all agent prompts.
    /// </summary>
    IReadOnlyList<AgentPrompt> AgentPrompts { get; }

    /// <summary>
    /// Adds an agent prompt to the store.
    /// </summary>
    Task AddAgentPromptAsync(AgentPrompt prompt);

    /// <summary>
    /// Updates an agent prompt in the store.
    /// </summary>
    Task UpdateAgentPromptAsync(AgentPrompt prompt);

    /// <summary>
    /// Removes an agent prompt from the store.
    /// </summary>
    Task RemoveAgentPromptAsync(string promptId);

    /// <summary>
    /// Gets an agent prompt by ID.
    /// </summary>
    AgentPrompt? GetAgentPrompt(string id);

    /// <summary>
    /// Gets agent prompts scoped to a specific project (not global prompts).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetAgentPromptsByProject(string projectId);

    #endregion

    /// <summary>
    /// Saves any pending changes to disk.
    /// </summary>
    Task SaveAsync();
}
