using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// One-shot startup probe of the mini-prompt sidecar. Logs a Warning when the
/// configured <see cref="MiniPromptOptions.SidecarUrl"/> is missing or unreachable
/// so that operators see the degradation immediately at boot rather than at the
/// first <c>/api/orchestration/generate-branch-id</c> request.
/// </summary>
public sealed class MiniPromptHealthCheckHostedService : IHostedService
{
    private readonly MiniPromptOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MiniPromptHealthCheckHostedService> _logger;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public MiniPromptHealthCheckHostedService(
        IOptions<MiniPromptOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<MiniPromptHealthCheckHostedService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarUrl))
        {
            _logger.LogWarning(
                "MiniPrompt sidecar URL is not configured (MiniPrompt:SidecarUrl). "
                + "AI branch-id generation will return a deterministic slug fallback.");
            return;
        }

        var probeUrl = $"{_options.SidecarUrl.TrimEnd('/')}/api/mini-prompt";

        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(ProbeTimeout);

            var client = _httpClientFactory.CreateClient("MiniPrompt");
            // GET against a POST-only endpoint returns 405 from a healthy sidecar — that still
            // proves the worker is reachable. Any non-success status that *isn't* a connection
            // failure is treated as "sidecar is up". A connection failure throws and we warn.
            using var response = await client.GetAsync(probeUrl, probeCts.Token);

            _logger.LogInformation(
                "MiniPrompt sidecar reachable at {ProbeUrl} (status {StatusCode}).",
                probeUrl, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown — don't warn.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MiniPrompt sidecar probe at {ProbeUrl} failed: {Error}. "
                + "AI branch-id generation will return a deterministic slug fallback "
                + "until the sidecar becomes reachable.",
                probeUrl, ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
