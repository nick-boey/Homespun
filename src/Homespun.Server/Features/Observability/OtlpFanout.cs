using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// Default <see cref="IOtlpFanout"/>. Re-serialises the scrubbed request to a
/// protobuf byte buffer once, then POSTs it to every configured destination in
/// parallel. Each leg's failure is logged at Warning and swallowed so the
/// controller returns 202 unconditionally.
/// </summary>
public sealed class OtlpFanout : IOtlpFanout
{
    internal const string HttpClientName = "otlp-fanout";
    internal const string ProtobufContentType = "application/x-protobuf";
    internal const string SeqApiKeyHeader = "X-Seq-ApiKey";
    internal const string AspireEndpointEnvKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    internal const string AspireHeadersEnvKey = "OTEL_EXPORTER_OTLP_HEADERS";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OtlpFanoutOptions> _options;
    private readonly ILogger<OtlpFanout> _logger;
    private readonly string? _aspireEndpoint;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _aspireHeaders;

    public OtlpFanout(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OtlpFanoutOptions> options,
        IConfiguration configuration,
        ILogger<OtlpFanout> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;

        var endpoint = configuration[AspireEndpointEnvKey];
        _aspireEndpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.TrimEnd('/');

        var rawHeaders = configuration[AspireHeadersEnvKey];
        _aspireHeaders = ParseHeaders(rawHeaders);
    }

    public Task LogsAsync(ExportLogsServiceRequest req, CancellationToken ct)
        => FanOutAsync(req, "/v1/logs", ct);

    public Task TracesAsync(ExportTraceServiceRequest req, CancellationToken ct)
        => FanOutAsync(req, "/v1/traces", ct);

    private async Task FanOutAsync(IMessage payload, string pathSuffix, CancellationToken ct)
    {
        var bytes = payload.ToByteArray();
        var opts = _options.CurrentValue;

        var tasks = new List<Task>(2);

        if (!string.IsNullOrWhiteSpace(opts.SeqBaseUrl))
        {
            tasks.Add(DispatchSafeAsync(
                destinationName: "seq",
                url: Combine(opts.SeqBaseUrl, pathSuffix),
                bytes: bytes,
                extraHeaders: BuildSeqHeaders(opts.SeqApiKey),
                ct));
        }

        if (_aspireEndpoint is not null)
        {
            tasks.Add(DispatchSafeAsync(
                destinationName: "aspire",
                url: Combine(_aspireEndpoint, pathSuffix),
                bytes: bytes,
                extraHeaders: _aspireHeaders,
                ct));
        }

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DispatchSafeAsync(
        string destinationName,
        string url,
        byte[] bytes,
        IReadOnlyList<KeyValuePair<string, string>> extraHeaders,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufContentType);
            foreach (var header in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = content;

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OTLP fan-out to {Destination} returned {StatusCode} for {Url}",
                    destinationName, (int)response.StatusCode, url);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled — propagation isn't useful; destination-level failure
            // already invariant-equivalent.
            _logger.LogWarning(
                "OTLP fan-out to {Destination} cancelled for {Url}", destinationName, url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OTLP fan-out to {Destination} failed for {Url}", destinationName, url);
        }
    }

    private static string Combine(string baseUrl, string pathSuffix)
        => baseUrl.TrimEnd('/') + pathSuffix;

    private static IReadOnlyList<KeyValuePair<string, string>> BuildSeqHeaders(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }
        return new[] { new KeyValuePair<string, string>(SeqApiKeyHeader, apiKey) };
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> ParseHeaders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        var result = new List<KeyValuePair<string, string>>();
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1)
            {
                continue;
            }
            var key = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }
            result.Add(new KeyValuePair<string, string>(key, value));
        }
        return result;
    }
}
