using System.Net.Http.Json;
using System.Web;
using Fleece.Core.Models;
using Homespun.Shared;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpIssueApiService(HttpClient http)
{
    public async Task<List<IssueResponse>> GetIssuesAsync(string projectId, IssueStatus? status = null,
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

        return await http.GetFromJsonAsync<List<IssueResponse>>(url) ?? [];
    }

    public async Task<List<IssueResponse>> GetReadyIssuesAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<IssueResponse>>(
            $"{ApiRoutes.Projects}/{projectId}/issues/ready") ?? [];
    }

    public async Task<IssueResponse?> GetIssueAsync(string issueId, string projectId)
    {
        var response = await http.GetAsync($"{ApiRoutes.Issues}/{issueId}?projectId={projectId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueResponse>();
    }

    public async Task<IssueResponse?> CreateIssueAsync(CreateIssueRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.Issues, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueResponse>();
    }

    public async Task<IssueResponse?> UpdateIssueAsync(string issueId, UpdateIssueRequest request)
    {
        var response = await http.PutAsJsonAsync($"{ApiRoutes.Issues}/{issueId}", request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssueResponse>();
    }

    public async Task DeleteIssueAsync(string issueId, string projectId)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Issues}/{issueId}?projectId={projectId}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Gets the resolved branch name for an issue by checking linked PRs and existing clones.
    /// </summary>
    /// <param name="issueId">The issue ID to resolve the branch for</param>
    /// <param name="projectId">The project ID</param>
    /// <returns>The resolved branch name, or null if no existing branch was found</returns>
    public async Task<string?> GetResolvedBranchAsync(string issueId, string projectId)
    {
        var response = await http.GetAsync($"{ApiRoutes.Issues}/{issueId}/resolved-branch?projectId={projectId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ResolvedBranchResponse>();
        return result?.BranchName;
    }
}

/// <summary>
/// Response model for resolved branch lookup.
/// </summary>
public class ResolvedBranchResponse
{
    public string? BranchName { get; set; }
}
