using System.IO.Compression;
using Google.Protobuf;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// OTLP/HTTP protobuf receiver. Acts as a mini-collector on behalf of the
/// worker and browser clients: parse → scrub → fan out to Seq + Aspire. Always
/// returns 202 to the caller once parsing succeeds so downstream unavailability
/// never triggers OTLP client retry storms.
/// </summary>
[ApiController]
[Route("api/otlp/v1")]
public sealed class OtlpReceiverController : ControllerBase
{
    internal const int MaxBodyBytes = 4 * 1024 * 1024;
    private const string ProtobufContentType = "application/x-protobuf";

    private readonly IOtlpScrubber _scrubber;
    private readonly IOtlpFanout _fanout;
    private readonly ILogger<OtlpReceiverController> _logger;

    public OtlpReceiverController(
        IOtlpScrubber scrubber,
        IOtlpFanout fanout,
        ILogger<OtlpReceiverController> logger)
    {
        _scrubber = scrubber;
        _fanout = fanout;
        _logger = logger;
    }

    [HttpPost("logs")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task<IActionResult> Logs(CancellationToken ct)
        => ReceiveAsync(
            parse: ExportLogsServiceRequest.Parser.ParseFrom,
            scrub: _scrubber.Scrub,
            dispatch: _fanout.LogsAsync,
            partialSuccess: new { partialSuccess = new { rejectedLogRecords = 0 } },
            ct);

    [HttpPost("traces")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task<IActionResult> Traces(CancellationToken ct)
        => ReceiveAsync(
            parse: ExportTraceServiceRequest.Parser.ParseFrom,
            scrub: _scrubber.Scrub,
            dispatch: _fanout.TracesAsync,
            partialSuccess: new { partialSuccess = new { rejectedSpans = 0 } },
            ct);

    private async Task<IActionResult> ReceiveAsync<T>(
        Func<Stream, T> parse,
        Action<T> scrub,
        Func<T, CancellationToken, Task> dispatch,
        object partialSuccess,
        CancellationToken ct)
        where T : IMessage<T>
    {
        if (!(Request.ContentType?.StartsWith(ProtobufContentType, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        // Allow the full 4 MiB despite the global default in Kestrel; the per-
        // endpoint RequestSizeLimit attribute above is already the hard cap.
        var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = MaxBodyBytes;
        }

        T request;
        try
        {
            // Buffer body fully via async I/O before parsing — Google.Protobuf's
            // ParseFrom(Stream) does synchronous reads, which Kestrel rejects by
            // default (AllowSynchronousIO=false).
            using var buffered = await ReadBodyAsync(ct).ConfigureAwait(false);
            request = parse(buffered);
        }
        catch (InvalidProtocolBufferException ex)
        {
            _logger.LogWarning(ex, "OTLP request rejected: malformed protobuf body");
            return BadRequest();
        }

        scrub(request);
        // Production IOtlpFanout swallows its own errors, but spec requires 202
        // even if a bespoke fanout implementation throws — keeps the OTLP client
        // retry loop from amplifying sink outages.
        try
        {
            await dispatch(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OTLP fan-out threw; responding 202 regardless");
        }

        return StatusCode(StatusCodes.Status202Accepted, partialSuccess);
    }

    private async Task<MemoryStream> ReadBodyAsync(CancellationToken ct)
    {
        var raw = new MemoryStream();
        await Request.Body.CopyToAsync(raw, ct).ConfigureAwait(false);
        raw.Position = 0;

        var encoding = Request.Headers.ContentEncoding;
        var gzipped = encoding.Count > 0 && encoding.Any(e =>
            string.Equals(e, "gzip", StringComparison.OrdinalIgnoreCase));
        if (!gzipped)
        {
            return raw;
        }

        var decompressed = new MemoryStream();
        await using (raw)
        await using (var gz = new GZipStream(raw, CompressionMode.Decompress, leaveOpen: false))
        {
            await gz.CopyToAsync(decompressed, ct).ConfigureAwait(false);
        }
        decompressed.Position = 0;
        return decompressed;
    }
}
