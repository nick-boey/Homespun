using System.Net.Http.Json;
using Homespun.Shared.Models.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Homespun.Client.Services.Observability;

/// <summary>
/// Telemetry service that sends data to Azure Application Insights.
/// Falls back to console logging if not configured or initialization fails.
/// </summary>
public class ApplicationInsightsTelemetryService : ITelemetryService
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<ApplicationInsightsTelemetryService> _logger;
    private readonly ConsoleTelemetryService _fallbackService;

    private bool _initialized;
    private bool _appInsightsEnabled;

    public ApplicationInsightsTelemetryService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        ILogger<ApplicationInsightsTelemetryService> logger,
        ConsoleTelemetryService fallbackService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _logger = logger;
        _fallbackService = fallbackService;
    }

    public bool IsEnabled => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            // Fetch telemetry configuration from the server
            var config = await _httpClient.GetFromJsonAsync<TelemetryConfigDto>("api/telemetryconfig");

            if (config is null || !config.IsEnabled || string.IsNullOrEmpty(config.ApplicationInsightsConnectionString))
            {
                _logger.LogInformation("[Telemetry] Application Insights not configured, using console logging");
                _initialized = true;
                _appInsightsEnabled = false;
                return;
            }

            // Initialize Application Insights via JS interop
            var success = await _jsRuntime.InvokeAsync<bool>(
                "appInsights.initialize",
                config.ApplicationInsightsConnectionString);

            if (success)
            {
                _logger.LogInformation("[Telemetry] Application Insights initialized successfully");
                _appInsightsEnabled = true;
            }
            else
            {
                _logger.LogWarning("[Telemetry] Application Insights initialization failed, using console logging");
                _appInsightsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Telemetry] Failed to initialize Application Insights, using console logging");
            _appInsightsEnabled = false;
        }

        _initialized = true;
    }

    public void TrackPageView(string pageName, string? uri = null)
    {
        if (!_initialized)
        {
            return;
        }

        if (_appInsightsEnabled)
        {
            try
            {
                _ = _jsRuntime.InvokeVoidAsync("appInsights.trackPageView", pageName, uri ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Telemetry] Failed to track page view via App Insights");
            }
        }

        // Always log to console as well for debugging
        _fallbackService.TrackPageView(pageName, uri);
    }

    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        if (!_initialized)
        {
            return;
        }

        if (_appInsightsEnabled)
        {
            try
            {
                _ = _jsRuntime.InvokeVoidAsync("appInsights.trackEvent", eventName, properties ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Telemetry] Failed to track event via App Insights");
            }
        }

        _fallbackService.TrackEvent(eventName, properties);
    }

    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        if (!_initialized)
        {
            return;
        }

        if (_appInsightsEnabled)
        {
            try
            {
                _ = _jsRuntime.InvokeVoidAsync(
                    "appInsights.trackException",
                    exception.Message,
                    exception.StackTrace ?? string.Empty,
                    properties ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Telemetry] Failed to track exception via App Insights");
            }
        }

        _fallbackService.TrackException(exception, properties);
    }

    public void TrackDependency(string type, string target, string name, TimeSpan duration, bool success, int? statusCode = null)
    {
        if (!_initialized)
        {
            return;
        }

        if (_appInsightsEnabled)
        {
            try
            {
                _ = _jsRuntime.InvokeVoidAsync(
                    "appInsights.trackDependency",
                    type,
                    target,
                    name,
                    duration.TotalMilliseconds,
                    success,
                    statusCode ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Telemetry] Failed to track dependency via App Insights");
            }
        }

        _fallbackService.TrackDependency(type, target, name, duration, success, statusCode);
    }
}
