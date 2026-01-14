using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCodeUI.Models;

namespace Homespun.Features.ClaudeCodeUI.Services;

/// <summary>
/// HTTP client for communicating with Claude Code UI server instances.
/// </summary>
public class ClaudeCodeUIClient : IClaudeCodeUIClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeCodeUIClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ClaudeCodeUIClient(HttpClient httpClient, ILogger<ClaudeCodeUIClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            // Claude Code UI responds to root path
            var response = await _httpClient.GetAsync($"{baseUrl}/", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<ClaudeCodeUIResponse> SendPromptAsync(
        string baseUrl,
        ClaudeCodeUIPromptRequest request,
        CancellationToken ct = default)
    {
        var response = new ClaudeCodeUIResponse();
        var textBuilder = new StringBuilder();

        try
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/agent")
            {
                Content = JsonContent.Create(request, options: _jsonOptions)
            };
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var httpResponse = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            httpResponse.EnsureSuccessStatusCode();

            using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var data = line[6..];
                    if (data == "[DONE]") break;

                    try
                    {
                        var evt = JsonSerializer.Deserialize<ClaudeCodeUIEvent>(data, _jsonOptions);
                        if (evt != null)
                        {
                            ProcessEvent(evt, response, textBuilder);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse SSE event: {Data}", data);
                    }
                }
            }

            response.Text = textBuilder.ToString();
            response.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending prompt to {BaseUrl}", baseUrl);
            response.Error = ex.Message;
            response.Success = false;
        }

        return response;
    }

    public async Task SendPromptNoWaitAsync(
        string baseUrl,
        ClaudeCodeUIPromptRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/agent")
            {
                Content = JsonContent.Create(request, options: _jsonOptions)
            };

            // Fire and forget - don't wait for response
            _ = _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending prompt (no wait) to {BaseUrl}", baseUrl);
        }
    }

    public async Task<List<ClaudeCodeUIProject>> GetProjectsAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{baseUrl}/api/projects", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<ClaudeCodeUIProject>>(_jsonOptions, ct) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting projects from {BaseUrl}", baseUrl);
        }

        return [];
    }

    public async Task<List<ClaudeCodeUISession>> GetSessionsAsync(
        string baseUrl,
        string projectPath,
        CancellationToken ct = default)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(projectPath);
            var response = await _httpClient.GetAsync($"{baseUrl}/api/sessions?project={encodedPath}", ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<ClaudeCodeUISession>>(_jsonOptions, ct) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions from {BaseUrl}", baseUrl);
        }

        return [];
    }

    public async IAsyncEnumerable<ClaudeCodeUIEvent> SubscribeToEventsAsync(
        string baseUrl,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = sessionId != null
            ? $"{baseUrl}/api/events?sessionId={sessionId}"
            : $"{baseUrl}/api/events";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]") break;

                ClaudeCodeUIEvent? evt = null;
                try
                {
                    evt = JsonSerializer.Deserialize<ClaudeCodeUIEvent>(data, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Failed to parse SSE event: {Data}", data);
                }

                if (evt != null)
                {
                    yield return evt;
                }
            }
        }
    }

    private void ProcessEvent(ClaudeCodeUIEvent evt, ClaudeCodeUIResponse response, StringBuilder textBuilder)
    {
        switch (evt.Type)
        {
            case ClaudeCodeUIEventTypes.SessionCreated:
                response.SessionId = evt.SessionId;
                break;

            case ClaudeCodeUIEventTypes.ClaudeResponse:
                if (!string.IsNullOrEmpty(evt.Text))
                {
                    textBuilder.Append(evt.Text);
                }
                break;

            case ClaudeCodeUIEventTypes.ClaudeComplete:
                response.Success = true;
                break;

            case ClaudeCodeUIEventTypes.ClaudeError:
                response.Error = evt.Error;
                response.Success = false;
                break;
        }
    }
}
