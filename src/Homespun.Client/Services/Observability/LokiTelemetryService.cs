using System.Net.Http.Json;
using System.Timers;
using Homespun.Shared.Models.Observability;
using Microsoft.Extensions.Logging;

namespace Homespun.Client.Services.Observability;

/// <summary>
/// Telemetry service that sends batched events to the server's ClientTelemetry endpoint.
/// The server logs these events to stdout where Promtail collects them for Loki.
/// </summary>
public class LokiTelemetryService : ITelemetryService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LokiTelemetryService> _logger;
    private readonly ConsoleTelemetryService _fallbackService;
    private readonly List<ClientTelemetryEvent> _eventBuffer = [];
    private readonly object _bufferLock = new();
    private readonly System.Timers.Timer _flushTimer;

    private const int MaxBatchSize = 50;
    private const int FlushIntervalMs = 5000;

    private string _sessionId = Guid.NewGuid().ToString("N")[..12];
    private bool _initialized;
    private bool _disposed;

    public LokiTelemetryService(
        HttpClient httpClient,
        ILogger<LokiTelemetryService> logger,
        ConsoleTelemetryService fallbackService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _fallbackService = fallbackService;

        _flushTimer = new System.Timers.Timer(FlushIntervalMs);
        _flushTimer.Elapsed += OnFlushTimerElapsed;
        _flushTimer.AutoReset = true;
    }

    public bool IsEnabled => _initialized;

    public Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _sessionId = Guid.NewGuid().ToString("N")[..12];
        _flushTimer.Start();
        _initialized = true;
        _logger.LogInformation("[Telemetry] Loki telemetry service initialized with session {SessionId}", _sessionId);

        return Task.CompletedTask;
    }

    public void TrackPageView(string pageName, string? uri = null)
    {
        if (!_initialized) return;

        var evt = new ClientTelemetryEvent
        {
            Type = TelemetryEventType.PageView,
            Name = pageName,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = uri is not null ? new Dictionary<string, string> { ["uri"] = uri } : null
        };

        EnqueueEvent(evt);
        _fallbackService.TrackPageView(pageName, uri);
    }

    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        if (!_initialized) return;

        var evt = new ClientTelemetryEvent
        {
            Type = TelemetryEventType.Event,
            Name = eventName,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = properties is not null ? new Dictionary<string, string>(properties) : null
        };

        EnqueueEvent(evt);
        _fallbackService.TrackEvent(eventName, properties);
    }

    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        if (!_initialized) return;

        var props = new Dictionary<string, string>
        {
            ["message"] = exception.Message,
            ["stackTrace"] = exception.StackTrace ?? string.Empty,
            ["type"] = exception.GetType().Name
        };

        if (properties is not null)
        {
            foreach (var kvp in properties)
            {
                props[kvp.Key] = kvp.Value;
            }
        }

        var evt = new ClientTelemetryEvent
        {
            Type = TelemetryEventType.Exception,
            Name = exception.GetType().Name,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = props
        };

        EnqueueEvent(evt);

        // Flush immediately for exceptions
        _ = FlushAsync();

        _fallbackService.TrackException(exception, properties);
    }

    public void TrackDependency(string type, string target, string name, TimeSpan duration, bool success, int? statusCode = null)
    {
        if (!_initialized) return;

        var evt = new ClientTelemetryEvent
        {
            Type = TelemetryEventType.Dependency,
            Name = name,
            Timestamp = DateTimeOffset.UtcNow,
            DurationMs = duration.TotalMilliseconds,
            Success = success,
            StatusCode = statusCode,
            Properties = new Dictionary<string, string>
            {
                ["type"] = type,
                ["target"] = target
            }
        };

        EnqueueEvent(evt);
        _fallbackService.TrackDependency(type, target, name, duration, success, statusCode);
    }

    private void EnqueueEvent(ClientTelemetryEvent evt)
    {
        bool shouldFlush;

        lock (_bufferLock)
        {
            _eventBuffer.Add(evt);
            shouldFlush = _eventBuffer.Count >= MaxBatchSize;
        }

        if (shouldFlush)
        {
            _ = FlushAsync();
        }
    }

    private void OnFlushTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        List<ClientTelemetryEvent> eventsToSend;

        lock (_bufferLock)
        {
            if (_eventBuffer.Count == 0)
            {
                return;
            }

            eventsToSend = new List<ClientTelemetryEvent>(_eventBuffer);
            _eventBuffer.Clear();
        }

        try
        {
            var batch = new ClientTelemetryBatch
            {
                SessionId = _sessionId,
                Events = eventsToSend
            };

            var response = await _httpClient.PostAsJsonAsync("api/clienttelemetry", batch);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Telemetry] Failed to send telemetry batch: {StatusCode}",
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Telemetry] Error sending telemetry batch");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _flushTimer.Stop();
        _flushTimer.Dispose();

        // Final flush on dispose
        lock (_bufferLock)
        {
            if (_eventBuffer.Count > 0)
            {
                _ = FlushAsync();
            }
        }
    }
}
