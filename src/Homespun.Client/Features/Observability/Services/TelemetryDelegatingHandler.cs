using System.Diagnostics;

namespace Homespun.Client.Services.Observability;

/// <summary>
/// HTTP message handler that tracks all outgoing HTTP requests as dependencies.
/// </summary>
public class TelemetryDelegatingHandler : DelegatingHandler
{
    private readonly ITelemetryService _telemetryService;

    public TelemetryDelegatingHandler(ITelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var target = request.RequestUri?.Host ?? "unknown";
        var name = $"{request.Method} {request.RequestUri?.PathAndQuery ?? "/"}";
        var success = false;
        int? statusCode = null;

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            success = response.IsSuccessStatusCode;
            statusCode = (int)response.StatusCode;

            _telemetryService.TrackDependency(
                type: "HTTP",
                target: target,
                name: name,
                duration: stopwatch.Elapsed,
                success: success,
                statusCode: statusCode);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _telemetryService.TrackDependency(
                type: "HTTP",
                target: target,
                name: name,
                duration: stopwatch.Elapsed,
                success: false,
                statusCode: null);

            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["RequestUri"] = request.RequestUri?.ToString() ?? "unknown",
                ["Method"] = request.Method.ToString()
            });

            throw;
        }
    }
}
