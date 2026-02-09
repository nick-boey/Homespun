using System.Net.Http.Json;
using Homespun.Shared;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;

namespace Homespun.Client.Services;

public class HttpSessionApiService(HttpClient http)
{
    public async Task<List<SessionSummary>> GetAllSessionsAsync()
    {
        return await http.GetFromJsonAsync<List<SessionSummary>>(ApiRoutes.Sessions) ?? [];
    }

    public async Task<ClaudeSession?> GetSessionAsync(string sessionId)
    {
        var response = await http.GetAsync($"{ApiRoutes.Sessions}/{sessionId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClaudeSession>();
    }

    public async Task<List<SessionSummary>> GetProjectSessionsAsync(string projectId)
    {
        return await http.GetFromJsonAsync<List<SessionSummary>>(
            $"{ApiRoutes.Sessions}/project/{projectId}") ?? [];
    }

    public async Task<ClaudeSession?> CreateSessionAsync(CreateSessionRequest request)
    {
        var response = await http.PostAsJsonAsync(ApiRoutes.Sessions, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClaudeSession>();
    }

    public async Task SendMessageAsync(string sessionId, SendMessageRequest request)
    {
        var response = await http.PostAsJsonAsync($"{ApiRoutes.Sessions}/{sessionId}/messages", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopSessionAsync(string sessionId)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Sessions}/{sessionId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task InterruptSessionAsync(string sessionId)
    {
        var response = await http.PostAsync($"{ApiRoutes.Sessions}/{sessionId}/interrupt", null);
        response.EnsureSuccessStatusCode();
    }
}
