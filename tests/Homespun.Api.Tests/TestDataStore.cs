using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Api.Tests;

/// <summary>
/// In-memory data store for testing.
/// </summary>
public class TestDataStore : IDataStore
{
    private readonly List<Project> _projects = [];
    private readonly List<PullRequest> _pullRequests = [];
    private readonly List<string> _favoriteModels = [];

    public IReadOnlyList<Project> Projects => _projects.AsReadOnly();
    public IReadOnlyList<PullRequest> PullRequests => _pullRequests.AsReadOnly();
    public IReadOnlyList<string> FavoriteModels => _favoriteModels.AsReadOnly();

    public Task AddProjectAsync(Project project)
    {
        _projects.Add(project);
        return Task.CompletedTask;
    }

    public Task UpdateProjectAsync(Project project)
    {
        var index = _projects.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            _projects[index] = project;
        }
        return Task.CompletedTask;
    }

    public Task RemoveProjectAsync(string projectId)
    {
        _projects.RemoveAll(p => p.Id == projectId);
        return Task.CompletedTask;
    }

    public Project? GetProject(string id)
    {
        return _projects.FirstOrDefault(p => p.Id == id);
    }

    public Task AddPullRequestAsync(PullRequest pullRequest)
    {
        _pullRequests.Add(pullRequest);
        return Task.CompletedTask;
    }

    public Task UpdatePullRequestAsync(PullRequest pullRequest)
    {
        var index = _pullRequests.FindIndex(pr => pr.Id == pullRequest.Id);
        if (index >= 0)
        {
            _pullRequests[index] = pullRequest;
        }
        return Task.CompletedTask;
    }

    public Task RemovePullRequestAsync(string pullRequestId)
    {
        _pullRequests.RemoveAll(pr => pr.Id == pullRequestId);
        return Task.CompletedTask;
    }

    public PullRequest? GetPullRequest(string id)
    {
        return _pullRequests.FirstOrDefault(pr => pr.Id == id);
    }

    public IReadOnlyList<PullRequest> GetPullRequestsByProject(string projectId)
    {
        return _pullRequests.Where(pr => pr.ProjectId == projectId).ToList().AsReadOnly();
    }

    public Task AddFavoriteModelAsync(string modelId)
    {
        if (!_favoriteModels.Contains(modelId))
        {
            _favoriteModels.Add(modelId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveFavoriteModelAsync(string modelId)
    {
        _favoriteModels.Remove(modelId);
        return Task.CompletedTask;
    }

    public bool IsFavoriteModel(string modelId)
    {
        return _favoriteModels.Contains(modelId);
    }

    public Task SaveAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clear all data (for test isolation).
    /// </summary>
    public void Clear()
    {
        _projects.Clear();
        _pullRequests.Clear();
        _favoriteModels.Clear();
    }

    /// <summary>
    /// Seed a project directly for testing.
    /// </summary>
    public void SeedProject(Project project)
    {
        _projects.Add(project);
    }

    /// <summary>
    /// Seed a pull request directly for testing.
    /// </summary>
    public void SeedPullRequest(PullRequest pullRequest)
    {
        _pullRequests.Add(pullRequest);
    }
}
