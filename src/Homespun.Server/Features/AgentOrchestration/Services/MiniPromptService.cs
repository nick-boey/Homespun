using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Service for executing lightweight AI prompts without tools.
/// Requires a sidecar worker with the /api/mini-prompt endpoint.
/// </summary>
public class MiniPromptService : IMiniPromptService
{
    private readonly MiniPromptOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MiniPromptService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MiniPromptService(
        IOptions<MiniPromptOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<MiniPromptService>? logger = null)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("MiniPrompt");
        _httpClient.Timeout = _options.RequestTimeout;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MiniPromptService>.Instance;
    }

    /// <inheritdoc />
    public async Task<MiniPromptResult> ExecuteAsync(
        string prompt,
        string model = "haiku",
        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
        }

        // Check cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();

        // Sidecar URL is required
        if (string.IsNullOrEmpty(_options.SidecarUrl))
        {
            throw new InvalidOperationException(
                "MiniPromptService requires a sidecar URL. Configure MiniPrompt:SidecarUrl in appsettings.");
        }

        return await ExecuteViaSidecarAsync(prompt, model, cancellationToken);
    }

    /// <summary>
    /// Executes the mini prompt via the sidecar worker HTTP endpoint.
    /// </summary>
    private async Task<MiniPromptResult> ExecuteViaSidecarAsync(
        string prompt,
        string model,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Executing mini-prompt via sidecar with model {Model}: {PromptPreview}",
                model, prompt.Length > 50 ? prompt[..50] + "..." : prompt);

            var requestBody = new SidecarMiniPromptRequest
            {
                Prompt = prompt,
                Model = model
            };

            var url = $"{_options.SidecarUrl!.TrimEnd('/')}/api/mini-prompt";
            using var response = await _httpClient.PostAsJsonAsync(url, requestBody, JsonOptions, cancellationToken);

            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Sidecar mini-prompt failed with status {StatusCode}: {Error}",
                    response.StatusCode, errorContent);

                return new MiniPromptResult(
                    Success: false,
                    Response: null,
                    Error: $"Sidecar returned {(int)response.StatusCode}: {errorContent}",
                    CostUsd: null,
                    DurationMs: durationMs);
            }

            var result = await response.Content.ReadFromJsonAsync<SidecarMiniPromptResponse>(JsonOptions, cancellationToken);

            if (result == null)
            {
                return new MiniPromptResult(
                    Success: false,
                    Response: null,
                    Error: "Empty response from sidecar",
                    CostUsd: null,
                    DurationMs: durationMs);
            }

            _logger.LogDebug("Mini-prompt via sidecar completed in {DurationMs}ms, cost: ${CostUsd}",
                result.DurationMs ?? durationMs, result.CostUsd?.ToString("F6") ?? "N/A");

            return new MiniPromptResult(
                Success: result.Success,
                Response: result.Response,
                Error: result.Error,
                CostUsd: result.CostUsd,
                DurationMs: result.DurationMs ?? durationMs);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Mini-prompt cancelled");
            throw new OperationCanceledException(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogWarning(ex, "Sidecar mini-prompt HTTP request failed after {DurationMs}ms: {Error}",
                durationMs, ex.Message);

            return new MiniPromptResult(
                Success: false,
                Response: null,
                Error: $"Sidecar connection failed: {ex.Message}",
                CostUsd: null,
                DurationMs: durationMs);
        }
        catch (Exception ex)
        {
            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogWarning(ex, "Sidecar mini-prompt failed after {DurationMs}ms: {Error}",
                durationMs, ex.Message);

            return new MiniPromptResult(
                Success: false,
                Response: null,
                Error: ex.Message,
                CostUsd: null,
                DurationMs: durationMs);
        }
    }

    /// <summary>
    /// Request body for the sidecar mini-prompt endpoint.
    /// </summary>
    private record SidecarMiniPromptRequest
    {
        public required string Prompt { get; init; }
        public string? Model { get; init; }
    }

    /// <summary>
    /// Response body from the sidecar mini-prompt endpoint.
    /// </summary>
    private record SidecarMiniPromptResponse
    {
        public bool Success { get; init; }
        public string? Response { get; init; }
        public string? Error { get; init; }
        public decimal? CostUsd { get; init; }
        public int? DurationMs { get; init; }
    }
}
