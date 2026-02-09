using System.Net.Http.Json;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Client.Services;

public record SessionCacheSummary
{
    public string SessionId { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public string? ProjectId { get; init; }
    public SessionMode? Mode { get; init; }
    public string? Model { get; init; }
    public int MessageCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastMessageAt { get; init; }
}

public class HttpSessionCacheApiService(HttpClient http)
{
    private const string BaseUrl = "api/session-cache";

    public async Task<List<ClaudeMessage>> GetMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<ClaudeMessage>>($"{BaseUrl}/{sessionId}/messages", ct) ?? [];
    }

    public async Task<SessionCacheSummary?> GetSummaryAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"{BaseUrl}/{sessionId}/summary", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionCacheSummary>(ct);
    }

    public async Task<List<SessionCacheSummary>> ListSessionsAsync(string projectId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<SessionCacheSummary>>(
            $"{BaseUrl}/project/{projectId}", ct) ?? [];
    }

    public async Task<List<string>> GetEntitySessionIdsAsync(string projectId, string entityId, CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<string>>(
            $"{BaseUrl}/entity/{projectId}/{entityId}", ct) ?? [];
    }
}
