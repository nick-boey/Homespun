using System.Net.Http.Json;
using Homespun.Shared.Models.GitHub;

namespace Homespun.Client.Services;

public record GitHubStatusInfo
{
    public bool IsConfigured { get; init; }
    public string? MaskedToken { get; init; }
}

public class HttpGitHubInfoApiService(HttpClient http)
{
    private const string BaseUrl = "api/github";

    public async Task<GitHubStatusInfo?> GetStatusAsync()
    {
        return await http.GetFromJsonAsync<GitHubStatusInfo>($"{BaseUrl}/status");
    }

    public async Task<GitHubAuthStatus?> GetAuthStatusAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<GitHubAuthStatus>($"{BaseUrl}/auth-status", ct);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var result = await GetAuthStatusAsync(ct);
        return result?.IsAuthenticated ?? false;
    }
}
