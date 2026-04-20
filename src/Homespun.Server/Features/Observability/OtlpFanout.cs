using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// Default <see cref="IOtlpFanout"/>. Re-serialises the scrubbed request to a
/// protobuf byte buffer once, then dispatches it to every configured
/// destination in parallel. Each leg's failure is logged at Warning and
/// swallowed so the controller returns 202 unconditionally.
///
/// The Seq leg is always HTTP/protobuf (the Seq ingest URL is configured
/// explicitly). The Aspire leg honours <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>:
/// <c>http/protobuf</c> (or unset) → plain POST to <c>{endpoint}/v1/{logs,traces}</c>;
/// <c>grpc</c> → HTTP/2 POST to <c>{endpoint}/{full-method}</c> with a
/// length-prefixed gRPC frame. Aspire's dashboard historically defaults to
/// gRPC, so supporting both is load-bearing for dev.
/// </summary>
public sealed class OtlpFanout : IOtlpFanout
{
    internal const string HttpClientName = "otlp-fanout";
    internal const string ProtobufContentType = "application/x-protobuf";
    internal const string GrpcProtoContentType = "application/grpc+proto";
    internal const string SeqApiKeyHeader = "X-Seq-ApiKey";
    internal const string AspireEndpointEnvKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    internal const string AspireHeadersEnvKey = "OTEL_EXPORTER_OTLP_HEADERS";
    internal const string AspireProtocolEnvKey = "OTEL_EXPORTER_OTLP_PROTOCOL";
    internal const string GrpcProtocol = "grpc";

    internal const string LogsHttpPath = "/v1/logs";
    internal const string TracesHttpPath = "/v1/traces";
    internal const string LogsGrpcMethod =
        "/opentelemetry.proto.collector.logs.v1.LogsService/Export";
    internal const string TracesGrpcMethod =
        "/opentelemetry.proto.collector.trace.v1.TraceService/Export";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OtlpFanoutOptions> _options;
    private readonly ILogger<OtlpFanout> _logger;
    private readonly string? _aspireEndpoint;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _aspireHeaders;
    private readonly bool _aspireUsesGrpc;

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

        var protocol = configuration[AspireProtocolEnvKey];
        _aspireUsesGrpc = !string.IsNullOrWhiteSpace(protocol)
            && string.Equals(protocol.Trim(), GrpcProtocol, StringComparison.OrdinalIgnoreCase);
    }

    public Task LogsAsync(ExportLogsServiceRequest req, CancellationToken ct)
        => FanOutAsync(req, LogsHttpPath, LogsGrpcMethod, ct);

    public Task TracesAsync(ExportTraceServiceRequest req, CancellationToken ct)
        => FanOutAsync(req, TracesHttpPath, TracesGrpcMethod, ct);

    private async Task FanOutAsync(
        IMessage payload,
        string httpPath,
        string grpcFullMethod,
        CancellationToken ct)
    {
        var bytes = payload.ToByteArray();
        var opts = _options.CurrentValue;

        var tasks = new List<Task>(2);

        if (!string.IsNullOrWhiteSpace(opts.SeqBaseUrl))
        {
            tasks.Add(DispatchHttpAsync(
                destinationName: "seq",
                url: Combine(opts.SeqBaseUrl, httpPath),
                bytes: bytes,
                extraHeaders: BuildSeqHeaders(opts.SeqApiKey),
                ct));
        }

        if (_aspireEndpoint is not null)
        {
            if (_aspireUsesGrpc)
            {
                tasks.Add(DispatchGrpcAsync(
                    destinationName: "aspire",
                    url: Combine(_aspireEndpoint, grpcFullMethod),
                    bytes: bytes,
                    extraHeaders: _aspireHeaders,
                    ct));
            }
            else
            {
                tasks.Add(DispatchHttpAsync(
                    destinationName: "aspire",
                    url: Combine(_aspireEndpoint, httpPath),
                    bytes: bytes,
                    extraHeaders: _aspireHeaders,
                    ct));
            }
        }

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DispatchHttpAsync(
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

    private async Task DispatchGrpcAsync(
        string destinationName,
        string url,
        byte[] bytes,
        IReadOnlyList<KeyValuePair<string, string>> extraHeaders,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var framed = EncodeGrpcFrame(bytes);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            var content = new ByteArrayContent(framed);
            content.Headers.TryAddWithoutValidation("Content-Type", GrpcProtoContentType);
            request.Content = content;
            request.Headers.TryAddWithoutValidation("TE", "trailers");
            foreach (var header in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OTLP fan-out to {Destination} returned HTTP {StatusCode} for {Url}",
                    destinationName, (int)response.StatusCode, url);
                return;
            }

            // Drain the body so the HTTP/2 trailers arrive.
            await response.Content.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);

            var grpcStatus = ReadGrpcStatus(response);
            if (grpcStatus != 0)
            {
                var grpcMessage = ReadGrpcMessage(response);
                _logger.LogWarning(
                    "OTLP fan-out to {Destination} gRPC status {Status} ({Message}) for {Url}",
                    destinationName, grpcStatus, grpcMessage, url);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
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

    internal static byte[] EncodeGrpcFrame(byte[] payload)
    {
        var framed = new byte[5 + payload.Length];
        framed[0] = 0; // compression flag: none
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1, 4), (uint)payload.Length);
        Buffer.BlockCopy(payload, 0, framed, 5, payload.Length);
        return framed;
    }

    private static int ReadGrpcStatus(HttpResponseMessage response)
    {
        if (TryReadStatus(response.TrailingHeaders, out var trailer))
        {
            return trailer;
        }
        // Some servers return trailers-only responses with grpc-status in headers.
        if (TryReadStatus(response.Headers, out var header))
        {
            return header;
        }
        return 0;
    }

    private static bool TryReadStatus(HttpHeaders headers, out int status)
    {
        if (headers.TryGetValues("grpc-status", out var values)
            && int.TryParse(values.FirstOrDefault(), out status))
        {
            return true;
        }
        status = 0;
        return false;
    }

    private static string ReadGrpcMessage(HttpResponseMessage response)
    {
        if (response.TrailingHeaders.TryGetValues("grpc-message", out var t))
        {
            return string.Join(",", t);
        }
        if (response.Headers.TryGetValues("grpc-message", out var h))
        {
            return string.Join(",", h);
        }
        return string.Empty;
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
