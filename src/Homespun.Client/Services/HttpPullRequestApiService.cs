using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.PullRequests;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpPullRequestApiService(HttpClient http)
{
    public async Task<List<PullRequest>> GetProjectPullRequestsAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<PullRequest>>(
            $"{ApiRoutes.Projects}/{projectId}/pull-requests") ?? [];
    }

    public async Task<PullRequest?> GetPullRequestAsync(string id)
    {
        var response = await http.GetAsync($"{ApiRoutes.PullRequests}/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PullRequest>();
    }

    public Task<PullRequest?> GetByIdAsync(string id) => GetPullRequestAsync(id);

    public async Task<PullRequest?> CreatePullRequestAsync(CreatePullRequestRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.PullRequests, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PullRequest>();
    }

    public async Task<PullRequest?> UpdatePullRequestAsync(string id, UpdatePullRequestRequest request)
    {
        var response = await http.PutAsJsonAsync($"{ApiRoutes.PullRequests}/{id}", request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PullRequest>();
    }

    public async Task DeletePullRequestAsync(string id)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.PullRequests}/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<SyncResult?> SyncPullRequestsAsync(string projectId)
    {
        var response = await http.PostAsync($"{ApiRoutes.Projects}/{projectId}/sync", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncResult>();
    }

    public async Task<List<PullRequestWithStatus>> GetOpenPullRequestsAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<PullRequestWithStatus>>(
            $"{ApiRoutes.Projects}/{projectId}/pull-requests/open") ?? [];
    }

    public async Task<List<PullRequestWithTime>> GetMergedPullRequestsAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<PullRequestWithTime>>(
            $"{ApiRoutes.Projects}/{projectId}/pull-requests/merged") ?? [];
    }
}
