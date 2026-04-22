using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Live adapter that calls the Anthropic REST <c>GET /v1/models</c> endpoint
/// directly over <see cref="HttpClient"/> and projects the paginated response
/// into <see cref="RawModelInfo"/>.
/// </summary>
/// <remarks>
/// We do not go through the official SDK's <c>IAnthropicClient</c> here because
/// the SDK's <c>Models.List</c> path fails with
/// <c>"OAuth authentication is currently not supported"</c> when the configured
/// <c>AuthToken</c> is a Claude Code OAuth token (<c>CLAUDE_CODE_OAUTH_TOKEN</c>,
/// <c>sk-ant-oat…</c>) even though the same token works against the REST
/// endpoint when sent verbatim as <c>x-api-key</c>.
/// </remarks>
internal sealed class AnthropicModelSource : IAnthropicModelSource
{
    internal const string HttpClientName = "Anthropic.Models";
    private const string ListUrl = "https://api.anthropic.com/v1/models";
    private const string ApiVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;

    public AnthropicModelSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<RawModelInfo>> ListAllAsync(CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")
            ?? throw new InvalidOperationException(
                "CLAUDE_CODE_OAUTH_TOKEN is not set; live model catalog requires a credential.");

        using var client = _httpClientFactory.CreateClient(HttpClientName);

        var results = new List<RawModelInfo>();
        string? afterId = null;
        do
        {
            var url = afterId is null ? ListUrl : $"{ListUrl}?after_id={Uri.EscapeDataString(afterId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", token);
            request.Headers.Add("anthropic-version", ApiVersion);

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<ModelListPage>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Anthropic /v1/models returned an empty body.");

            foreach (var entry in page.Data)
            {
                results.Add(new RawModelInfo(entry.Id, entry.DisplayName, entry.CreatedAt));
            }

            afterId = page.HasMore ? page.LastId : null;
        } while (afterId is not null);

        return results;
    }

    private sealed record ModelListPage(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelEntry> Data,
        [property: JsonPropertyName("has_more")] bool HasMore,
        [property: JsonPropertyName("last_id")] string? LastId);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
}
