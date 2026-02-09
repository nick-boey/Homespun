using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Git;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpCloneApiService(HttpClient http)
{
    public async Task<List<CloneInfo>> ListClonesAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<CloneInfo>>(
            $"{ApiRoutes.Clones}?projectId={projectId}") ?? [];
    }

    public async Task<CreateCloneResponse?> CreateCloneAsync(CreateCloneRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.Clones, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateCloneResponse>();
    }

    public async Task DeleteCloneAsync(string projectId, string clonePath)
    {
        var response = await http.DeleteAsync(
            $"{ApiRoutes.Clones}?projectId={projectId}&clonePath={Uri.EscapeDataString(clonePath)}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<CloneExistsResponse?> CheckCloneExistsAsync(string projectId, string branchName)
    {
        return await http.GetFromJsonAsync<CloneExistsResponse>(
            $"{ApiRoutes.Clones}/exists?projectId={projectId}&branchName={Uri.EscapeDataString(branchName)}");
    }

    public async Task PruneClonesAsync(string projectId)
    {
        var response = await http.PostAsync(
            $"{ApiRoutes.Clones}/prune?projectId={projectId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PullCloneAsync(string clonePath)
    {
        var response = await http.PostAsync(
            $"{ApiRoutes.Clones}/pull?clonePath={Uri.EscapeDataString(clonePath)}", null);
        response.EnsureSuccessStatusCode();
    }
}
