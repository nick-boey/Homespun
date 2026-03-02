using System.Net.Http.Json;
using Homespun.Shared.Models.GitHub;

namespace Homespun.Client.Services;

public class HttpIssuePrStatusApiService(HttpClient http)
{
    public async Task<IssuePullRequestStatus?> GetStatusAsync(string projectId, string issueId)
    {
        var response = await http.GetAsync($"api/issue-pr-status/{projectId}/{issueId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssuePullRequestStatus>();
    }
}
