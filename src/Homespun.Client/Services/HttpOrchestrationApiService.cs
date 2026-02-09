using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpOrchestrationApiService(HttpClient http)
{
    public async Task<GenerateBranchIdResponse?> GenerateBranchIdAsync(GenerateBranchIdRequest request)
    {
        var response = await http.PostAsJsonAsync($"{ApiRoutes.Orchestration}/generate-branch-id", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>();
    }
}
