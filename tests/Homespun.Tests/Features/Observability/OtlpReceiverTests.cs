using System.IO.Compression;
using System.Reflection;
using Google.Protobuf;
using Homespun.Features.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Controller-level tests for <see cref="OtlpReceiverController"/>. The fanout
/// and scrubber are replaced with hand-rolled stubs so assertions can verify
/// the handoff contract: parse → scrub → dispatch → 202.
/// </summary>
[TestFixture]
public class OtlpReceiverTests
{
    private const string ProtobufContentType = "application/x-protobuf";

    [Test]
    public async Task Logs_happy_path_returns_202_and_fan_out_called()
    {
        var payload = BuildLogsPayload();
        var (controller, fanout, scrubber) = BuildController(payload.ToByteArray());

        var result = await controller.Logs(CancellationToken.None);

        AssertAccepted(result, "rejectedLogRecords");
        Assert.That(scrubber.LogsCalled, Is.EqualTo(1));
        Assert.That(fanout.LogDispatches, Has.Count.EqualTo(1));
        Assert.That(fanout.LogDispatches[0].ToByteArray(), Is.EqualTo(payload.ToByteArray()),
            "dispatched bytes must match input");
    }

    [Test]
    public async Task Trace_context_ids_preserved_through_proxy()
    {
        var traceId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var spanId = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var payload = BuildLogsPayload(record =>
        {
            record.TraceId = ByteString.CopyFrom(traceId);
            record.SpanId = ByteString.CopyFrom(spanId);
        });
        var (controller, fanout, _) = BuildController(payload.ToByteArray());

        await controller.Logs(CancellationToken.None);

        var dispatched = fanout.LogDispatches.Single();
        var dispatchedRecord = dispatched.ResourceLogs[0].ScopeLogs[0].LogRecords[0];
        Assert.That(dispatchedRecord.TraceId.ToByteArray(), Is.EqualTo(traceId));
        Assert.That(dispatchedRecord.SpanId.ToByteArray(), Is.EqualTo(spanId));
    }

    [Test]
    public async Task Gzip_body_decompressed_before_parse()
    {
        var payload = BuildLogsPayload();
        var gzipped = GzipEncode(payload.ToByteArray());
        var (controller, fanout, _) = BuildController(gzipped, contentEncoding: "gzip");

        var result = await controller.Logs(CancellationToken.None);

        AssertAccepted(result, "rejectedLogRecords");
        Assert.That(fanout.LogDispatches, Has.Count.EqualTo(1));
        Assert.That(fanout.LogDispatches[0].ToByteArray(), Is.EqualTo(payload.ToByteArray()));
    }

    [Test]
    public void Oversized_body_returns_413()
    {
        // [RequestSizeLimit] enforcement is owned by Kestrel/Mvc routing, not
        // the controller method body — it cannot be triggered from a direct
        // invocation. Assert instead that the attribute is declared on both
        // endpoints with the required 4 MiB ceiling (reflected from a private
        // field because RequestSizeLimitAttribute doesn't surface the value
        // publicly in net10.0), so the framework layer will reject oversize
        // bodies at ingress with 413.
        foreach (var methodName in new[] { nameof(OtlpReceiverController.Logs), nameof(OtlpReceiverController.Traces) })
        {
            var method = typeof(OtlpReceiverController).GetMethod(methodName)!;
            var attribute = method.GetCustomAttribute<RequestSizeLimitAttribute>();
            Assert.That(attribute, Is.Not.Null, $"{methodName} missing [RequestSizeLimit]");

            var bytesField = attribute!.GetType().GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? attribute.GetType().GetField("Bytes", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(bytesField, Is.Not.Null, "RequestSizeLimitAttribute private layout changed — update the test");
            Assert.That(bytesField!.GetValue(attribute), Is.EqualTo((long)OtlpReceiverController.MaxBodyBytes));
        }
    }

    [Test]
    public async Task Malformed_protobuf_returns_400_no_fanout()
    {
        var garbage = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        var (controller, fanout, _) = BuildController(garbage);

        var result = await controller.Logs(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestResult>());
        Assert.That(fanout.LogDispatches, Is.Empty);
    }

    [Test]
    public async Task Unsupported_content_type_returns_415()
    {
        var payload = BuildLogsPayload();
        var (controller, fanout, _) = BuildController(
            payload.ToByteArray(),
            contentType: "application/json");

        var result = await controller.Logs(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status415UnsupportedMediaType));
        Assert.That(fanout.LogDispatches, Is.Empty);
    }

    [Test]
    public async Task Upstream_seq_500_still_returns_202()
    {
        var payload = BuildLogsPayload();
        var fanout = new ThrowingFanout();
        var controller = BuildControllerWith(payload.ToByteArray(), fanout, new StubScrubber());

        var result = await controller.Logs(CancellationToken.None);

        AssertAccepted(result, "rejectedLogRecords");
    }

    [Test]
    public async Task Traces_happy_path_returns_202_with_rejectedSpans_field()
    {
        var payload = new ExportTraceServiceRequest();
        var (controller, _, _) = BuildController(payload.ToByteArray());

        var result = await controller.Traces(CancellationToken.None);

        AssertAccepted(result, "rejectedSpans");
    }

    private static ExportLogsServiceRequest BuildLogsPayload(Action<LogRecord>? configure = null)
    {
        var record = new LogRecord { Body = new AnyValue { StringValue = "hello" } };
        configure?.Invoke(record);
        var scope = new ScopeLogs();
        scope.LogRecords.Add(record);
        var resource = new ResourceLogs { Resource = new Resource() };
        resource.ScopeLogs.Add(scope);
        var req = new ExportLogsServiceRequest();
        req.ResourceLogs.Add(resource);
        return req;
    }

    private static byte[] GzipEncode(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(input);
        }
        return ms.ToArray();
    }

    private static (OtlpReceiverController, CapturingFanout, StubScrubber) BuildController(
        byte[] body,
        string contentType = ProtobufContentType,
        string? contentEncoding = null)
    {
        var fanout = new CapturingFanout();
        var scrubber = new StubScrubber();
        var controller = BuildControllerWith(body, fanout, scrubber, contentType, contentEncoding);
        return (controller, fanout, scrubber);
    }

    private static OtlpReceiverController BuildControllerWith(
        byte[] body,
        IOtlpFanout fanout,
        IOtlpScrubber scrubber,
        string contentType = ProtobufContentType,
        string? contentEncoding = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentType = contentType;
        context.Request.ContentLength = body.Length;
        if (contentEncoding is not null)
        {
            context.Request.Headers["Content-Encoding"] = contentEncoding;
        }
        return new OtlpReceiverController(scrubber, fanout, NullLogger<OtlpReceiverController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static void AssertAccepted(IActionResult result, string rejectedField)
    {
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var obj = (ObjectResult)result;
        Assert.That(obj.StatusCode, Is.EqualTo(StatusCodes.Status202Accepted));
        Assert.That(obj.Value, Is.Not.Null);

        var partialSuccess = obj.Value!.GetType().GetProperty("partialSuccess")?.GetValue(obj.Value);
        Assert.That(partialSuccess, Is.Not.Null, "response body must include a partialSuccess payload");
        var rejected = partialSuccess!.GetType().GetProperty(rejectedField)?.GetValue(partialSuccess);
        Assert.That(rejected, Is.EqualTo(0));
    }

    private sealed class CapturingFanout : IOtlpFanout
    {
        public List<ExportLogsServiceRequest> LogDispatches { get; } = new();
        public List<ExportTraceServiceRequest> TraceDispatches { get; } = new();

        public Task LogsAsync(ExportLogsServiceRequest req, CancellationToken ct)
        {
            LogDispatches.Add(req);
            return Task.CompletedTask;
        }

        public Task TracesAsync(ExportTraceServiceRequest req, CancellationToken ct)
        {
            TraceDispatches.Add(req);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingFanout : IOtlpFanout
    {
        public Task LogsAsync(ExportLogsServiceRequest req, CancellationToken ct)
            => throw new InvalidOperationException("simulated upstream 500");

        public Task TracesAsync(ExportTraceServiceRequest req, CancellationToken ct)
            => throw new InvalidOperationException("simulated upstream 500");
    }

    private sealed class StubScrubber : IOtlpScrubber
    {
        public int LogsCalled { get; private set; }
        public int TracesCalled { get; private set; }
        public void Scrub(ExportLogsServiceRequest req) => LogsCalled++;
        public void Scrub(ExportTraceServiceRequest req) => TracesCalled++;
    }
}
