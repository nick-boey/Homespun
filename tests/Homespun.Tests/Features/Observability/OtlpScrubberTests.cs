using Google.Protobuf;
using Homespun.Features.Observability;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Homespun.Tests.Features.Observability;

/// <summary>
/// Unit tests covering the two redaction rules <see cref="OtlpScrubber"/> owns:
/// content-preview gating by <c>SessionEventContent:ContentPreviewChars</c> and
/// secret-substring attribute-key redaction.
/// </summary>
[TestFixture]
public class OtlpScrubberTests
{
    private static OtlpScrubber BuildScrubber(int contentPreviewChars, params string[] secretSubstrings)
    {
        var contentOptions = new TestOptionsMonitor<SessionEventContentOptions>(
            new SessionEventContentOptions { ContentPreviewChars = contentPreviewChars });
        var scrubberOptions = new TestOptionsMonitor<OtlpScrubberOptions>(
            new OtlpScrubberOptions
            {
                SecretSubstrings = secretSubstrings.Length == 0
                    ? new List<string> { "authorization" }
                    : secretSubstrings.ToList(),
            });
        return new OtlpScrubber(contentOptions, scrubberOptions);
    }

    private static ExportLogsServiceRequest BuildLogsRequest(params KeyValue[] attributes)
    {
        var record = new LogRecord();
        record.Attributes.AddRange(attributes);
        var scope = new ScopeLogs();
        scope.LogRecords.Add(record);
        var resource = new ResourceLogs();
        resource.ScopeLogs.Add(scope);
        var req = new ExportLogsServiceRequest();
        req.ResourceLogs.Add(resource);
        return req;
    }

    [Test]
    public void Content_preview_truncated_per_config()
    {
        var chars = 10;
        var scrubber = BuildScrubber(chars);
        var longText = new string('a', chars * 3);
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "homespun.content.preview",
            Value = new AnyValue { StringValue = longText },
        });

        scrubber.Scrub(req);

        var attr = req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes[0];
        Assert.That(attr.Value.StringValue.Length, Is.EqualTo(chars + 1), "truncated string + ellipsis");
        Assert.That(attr.Value.StringValue, Does.EndWith("\u2026"));
    }

    [Test]
    public void Content_preview_shorter_than_limit_is_left_untouched()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 80);
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "homespun.content.preview",
            Value = new AnyValue { StringValue = "hi" },
        });

        scrubber.Scrub(req);

        Assert.That(req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes[0].Value.StringValue,
            Is.EqualTo("hi"));
    }

    [Test]
    public void Content_preview_removed_when_chars_zero()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 0);
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "homespun.content.preview",
            Value = new AnyValue { StringValue = "anything" },
        });

        scrubber.Scrub(req);

        Assert.That(req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes, Is.Empty);
    }

    [Test]
    public void Authorization_attribute_redacted()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 80, secretSubstrings: "authorization");
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "http.request.header.AUTHORIZATION",
            Value = new AnyValue { StringValue = "Bearer super-secret-token" },
        });

        scrubber.Scrub(req);

        var attr = req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes[0];
        Assert.That(attr.Value.StringValue, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Secret_match_clears_non_string_value_kind()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 80, secretSubstrings: "token");
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "auth.token.count",
            Value = new AnyValue { IntValue = 12345 },
        });

        scrubber.Scrub(req);

        var attr = req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes[0];
        Assert.That(attr.Value.ValueCase, Is.EqualTo(AnyValue.ValueOneofCase.StringValue));
        Assert.That(attr.Value.StringValue, Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void Non_secret_attribute_is_left_untouched()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 80, secretSubstrings: "token");
        var req = BuildLogsRequest(new KeyValue
        {
            Key = "http.status_code",
            Value = new AnyValue { IntValue = 200 },
        });

        scrubber.Scrub(req);

        var attr = req.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes[0];
        Assert.That(attr.Value.IntValue, Is.EqualTo(200));
    }

    [Test]
    public void Trace_scrubber_covers_span_and_event_attributes()
    {
        var scrubber = BuildScrubber(contentPreviewChars: 0, secretSubstrings: "secret");

        var span = new Span();
        span.Attributes.Add(new KeyValue
        {
            Key = "my.secret.key",
            Value = new AnyValue { StringValue = "abc" },
        });
        span.Attributes.Add(new KeyValue
        {
            Key = "homespun.content.preview",
            Value = new AnyValue { StringValue = "should-be-removed" },
        });

        var evt = new Span.Types.Event();
        evt.Attributes.Add(new KeyValue
        {
            Key = "nested.SECRET",
            Value = new AnyValue { StringValue = "xyz" },
        });
        span.Events.Add(evt);

        var scope = new ScopeSpans();
        scope.Spans.Add(span);
        var resource = new ResourceSpans();
        resource.ScopeSpans.Add(scope);
        var req = new ExportTraceServiceRequest();
        req.ResourceSpans.Add(resource);

        scrubber.Scrub(req);

        var scrubbedSpan = req.ResourceSpans[0].ScopeSpans[0].Spans[0];
        Assert.That(scrubbedSpan.Attributes, Has.Count.EqualTo(1),
            "content.preview should be removed when Chars == 0");
        Assert.That(scrubbedSpan.Attributes[0].Key, Is.EqualTo("my.secret.key"));
        Assert.That(scrubbedSpan.Attributes[0].Value.StringValue, Is.EqualTo("[REDACTED]"));
        Assert.That(scrubbedSpan.Events[0].Attributes[0].Value.StringValue, Is.EqualTo("[REDACTED]"));
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
