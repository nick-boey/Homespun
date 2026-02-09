using System.Net.Http.Json;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Client.Services;

public class HttpFleeceSyncApiService(HttpClient http)
{
    private const string BaseUrl = "api/fleece-sync";

    public async Task<BranchStatusResult?> GetBranchStatusAsync(string projectId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<BranchStatusResult>($"{BaseUrl}/{projectId}/branch-status", ct);
    }

    public async Task<FleeceIssueSyncResult?> SyncAsync(string projectId, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"{BaseUrl}/{projectId}/sync", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FleeceIssueSyncResult>(ct);
    }

    public Task<FleeceIssueSyncResult?> PullChangesAsync(string projectId, CancellationToken ct = default)
        => SyncAsync(projectId, ct);
}
