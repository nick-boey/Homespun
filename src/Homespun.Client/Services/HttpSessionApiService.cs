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

    public async Task<ClaudeSession?> GetSessionByEntityIdAsync(string entityId)
    {
        var response = await http.GetAsync($"{ApiRoutes.Sessions}/entity/{Uri.EscapeDataString(entityId)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClaudeSession>();
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

    public async Task SendMessageAsync(string sessionId, string message)
    {
        await SendMessageAsync(sessionId, new SendMessageRequest { Message = message });
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

    public async Task<ClaudeSession?> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null)
    {
        return await CreateSessionAsync(new CreateSessionRequest
        {
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = mode,
            Model = model,
            SystemPrompt = systemPrompt
        });
    }

    public async Task<ClaudeSession?> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory)
    {
        var response = await http.PostAsJsonAsync(
            $"{ApiRoutes.Sessions}/{sessionId}/resume",
            new ResumeSessionRequest
            {
                EntityId = entityId,
                ProjectId = projectId,
                WorkingDirectory = workingDirectory
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClaudeSession>();
    }

    public async Task<List<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory)
    {
        return await http.GetFromJsonAsync<List<ResumableSession>>(
            $"{ApiRoutes.Sessions}/entity/{Uri.EscapeDataString(entityId)}/resumable?workingDirectory={Uri.EscapeDataString(workingDirectory)}") ?? [];
    }

    public async Task<int> StopAllSessionsForEntityAsync(string entityId)
    {
        var response = await http.DeleteAsync($"{ApiRoutes.Sessions}/entity/{Uri.EscapeDataString(entityId)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<int>();
    }

    public async Task<List<ClaudeMessage>> GetCachedMessagesAsync(string sessionId)
    {
        return await http.GetFromJsonAsync<List<ClaudeMessage>>(
            $"{ApiRoutes.Sessions}/{sessionId}/cached-messages") ?? [];
    }

    public async Task<List<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId,
        string entityId)
    {
        return await http.GetFromJsonAsync<List<SessionCacheSummary>>(
            $"{ApiRoutes.Sessions}/history/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(entityId)}") ?? [];
    }
}
