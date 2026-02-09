using System.Net.Http.Json;
using System.Web;
using Homespun.Shared;
using Homespun.Shared.Requests;
using Fleece.Core.Models;

namespace Homespun.Client.Services;

public class HttpIssueApiService(HttpClient http)
{
    public async Task<List<Issue>> GetIssuesAsync(string projectId, IssueStatus? status = null,
        IssueType? type = null, int? priority = null)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (status.HasValue) query["status"] = status.Value.ToString();
        if (type.HasValue) query["type"] = type.Value.ToString();
        if (priority.HasValue) query["priority"] = priority.Value.ToString();

        var queryString = query.ToString();
        var url = $"{ApiRoutes.Projects}/{projectId}/issues";
        if (!string.IsNullOrEmpty(queryString))
            url += $"?{queryString}";

        return await http.GetFromJsonAsync<List<Issue>>(url) ?? [];
    }

    public async Task<List<Issue>> GetReadyIssuesAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<Issue>>(
            $"{ApiRoutes.Projects}/{projectId}/issues/ready") ?? [];
    }

    public async Task<Issue?> GetIssueAsync(string issueId, string projectId)
    {
        var response = await http.GetAsync($"{ApiRoutes.Issues}/{issueId}?projectId={projectId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Issue>();
    }

    public async Task<Issue?> CreateIssueAsync(CreateIssueRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.Issues, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Issue>();
    }

    public async Task<Issue?> UpdateIssueAsync(string issueId, UpdateIssueRequest request)
    {
        var response = await http.PutAsJsonAsync($"{ApiRoutes.Issues}/{issueId}", request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Issue>();
    }

    public async Task DeleteIssueAsync(string issueId, string projectId)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Issues}/{issueId}?projectId={projectId}");
        response.EnsureSuccessStatusCode();
    }
}
