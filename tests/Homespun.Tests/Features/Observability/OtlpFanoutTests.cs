using System.Net;
using Google.Protobuf;
using Homespun.Features.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Unit tests for <see cref="OtlpFanout"/>. A capturing
/// <see cref="HttpMessageHandler"/> records every outbound dispatch so each
/// assertion operates on the bytes and headers the upstream sinks would see.
/// </summary>
[TestFixture]
public class OtlpFanoutTests
{
    [Test]
    public async Task Empty_url_destination_is_skipped_silently()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(handler, seqBaseUrl: null, seqApiKey: null, aspireEndpoint: null);

        await fanout.LogsAsync(new ExportLogsServiceRequest(), CancellationToken.None);

        Assert.That(handler.Captured, Is.Empty, "no legs configured → no dispatches");
    }

    [Test]
    public async Task Seq_leg_sends_X_Seq_ApiKey_header()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: "http://seq:5341/ingest/otlp",
            seqApiKey: "my-seq-key",
            aspireEndpoint: null);

        await fanout.LogsAsync(new ExportLogsServiceRequest(), CancellationToken.None);

        Assert.That(handler.Captured, Has.Count.EqualTo(1));
        var captured = handler.Captured[0];
        Assert.That(captured.Uri.AbsoluteUri, Is.EqualTo("http://seq:5341/ingest/otlp/v1/logs"));
        Assert.That(captured.Headers.TryGetValues("X-Seq-ApiKey", out var values), Is.True);
        Assert.That(values!.Single(), Is.EqualTo("my-seq-key"));
    }

    [Test]
    public async Task Seq_leg_omits_ApiKey_header_when_key_unset()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: "http://seq:5341/ingest/otlp",
            seqApiKey: "",
            aspireEndpoint: null);

        await fanout.LogsAsync(new ExportLogsServiceRequest(), CancellationToken.None);

        Assert.That(handler.Captured[0].Headers.Contains("X-Seq-ApiKey"), Is.False);
    }

    [Test]
    public async Task Aspire_leg_skipped_when_OTEL_EXPORTER_OTLP_ENDPOINT_unset()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: "http://seq:5341/ingest/otlp",
            seqApiKey: null,
            aspireEndpoint: null);

        await fanout.LogsAsync(new ExportLogsServiceRequest(), CancellationToken.None);

        Assert.That(handler.Captured, Has.Count.EqualTo(1));
        Assert.That(handler.Captured[0].Uri.Host, Is.EqualTo("seq"));
    }

    [Test]
    public async Task Aspire_leg_forwards_OTEL_EXPORTER_OTLP_HEADERS_pairs()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: null,
            seqApiKey: null,
            aspireEndpoint: "http://dashboard:18889",
            aspireHeaders: "x-otlp-api-key=abc,x-extra=123");

        await fanout.TracesAsync(new ExportTraceServiceRequest(), CancellationToken.None);

        Assert.That(handler.Captured, Has.Count.EqualTo(1));
        var captured = handler.Captured[0];
        Assert.That(captured.Uri.AbsoluteUri, Is.EqualTo("http://dashboard:18889/v1/traces"));
        Assert.That(captured.Headers.TryGetValues("x-otlp-api-key", out var keyValues), Is.True);
        Assert.That(keyValues!.Single(), Is.EqualTo("abc"));
        Assert.That(captured.Headers.TryGetValues("x-extra", out var extraValues), Is.True);
        Assert.That(extraValues!.Single(), Is.EqualTo("123"));
    }

    [Test]
    public async Task Both_legs_dispatch_in_parallel_with_byte_identical_payloads()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: "http://seq/ingest/otlp",
            seqApiKey: null,
            aspireEndpoint: "http://dashboard:18889");

        var payload = new ExportLogsServiceRequest();
        var originalBytes = payload.ToByteArray();
        await fanout.LogsAsync(payload, CancellationToken.None);

        Assert.That(handler.Captured, Has.Count.EqualTo(2));
        foreach (var captured in handler.Captured)
        {
            Assert.That(captured.Body, Is.EqualTo(originalBytes),
                "scrubbed payload must reach each leg unmodified");
            Assert.That(captured.ContentType, Is.EqualTo("application/x-protobuf"));
        }
    }

    [Test]
    public async Task Upstream_500_is_logged_and_swallowed()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var fanout = BuildFanout(
            handler,
            seqBaseUrl: "http://seq/ingest/otlp",
            seqApiKey: null,
            aspireEndpoint: null);

        Assert.DoesNotThrowAsync(() => fanout.LogsAsync(new ExportLogsServiceRequest(), CancellationToken.None));
    }

    [Test]
    public void Header_parser_skips_malformed_entries()
    {
        var parsed = OtlpFanout.ParseHeaders("a=1,=novalue,keyonly=,b=2");
        Assert.That(parsed, Is.EquivalentTo(new[]
        {
            new KeyValuePair<string, string>("a", "1"),
            new KeyValuePair<string, string>("b", "2"),
        }));
    }

    private static OtlpFanout BuildFanout(
        CapturingHandler handler,
        string? seqBaseUrl,
        string? seqApiKey,
        string? aspireEndpoint,
        string? aspireHeaders = null)
    {
        var httpClientFactory = new StubHttpClientFactory(handler);
        var options = new TestOptionsMonitor<OtlpFanoutOptions>(new OtlpFanoutOptions
        {
            SeqBaseUrl = seqBaseUrl,
            SeqApiKey = seqApiKey,
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OtlpFanout.AspireEndpointEnvKey] = aspireEndpoint,
                [OtlpFanout.AspireHeadersEnvKey] = aspireHeaders,
            })
            .Build();
        return new OtlpFanout(httpClientFactory, options, config, NullLogger<OtlpFanout>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public List<CapturedRequest> Captured { get; } = new();

        public CapturingHandler(HttpStatusCode status) => _status = status;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyBytes = request.Content is not null
                ? await request.Content.ReadAsByteArrayAsync(cancellationToken)
                : Array.Empty<byte>();

            // Snapshot headers; HttpRequestMessage is disposed after SendAsync returns.
            var headers = new HttpResponseMessage().Headers;
            foreach (var header in request.Headers)
            {
                headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Captured.Add(new CapturedRequest(
                Uri: request.RequestUri!,
                Body: bodyBytes,
                ContentType: request.Content?.Headers.ContentType?.MediaType ?? string.Empty,
                Headers: headers));

            return new HttpResponseMessage(_status);
        }
    }

    private sealed record CapturedRequest(
        Uri Uri,
        byte[] Body,
        string ContentType,
        System.Net.Http.Headers.HttpHeaders Headers);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
