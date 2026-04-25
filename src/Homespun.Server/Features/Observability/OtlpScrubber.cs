using Google.Protobuf.Collections;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// Default <see cref="IOtlpScrubber"/>. Walks the request's attribute trees
/// once and applies the two redaction rules required by the proxy spec:
///   1. <c>homespun.content.preview</c> → truncate to
///      <see cref="SessionEventContentOptions.ContentPreviewChars"/>; remove
///      when <c>Chars == 0</c>.
///   2. Secret-substring match on attribute key → replace string value with
///      <c>[REDACTED]</c>, clear non-string value kinds.
/// </summary>
public sealed class OtlpScrubber : IOtlpScrubber
{
    internal const string ContentPreviewAttributeKey = "homespun.content.preview";
    internal const string RedactedValue = "[REDACTED]";

    private readonly IOptionsMonitor<SessionEventContentOptions> _contentOptions;
    private readonly IOptionsMonitor<OtlpScrubberOptions> _scrubberOptions;

    public OtlpScrubber(
        IOptionsMonitor<SessionEventContentOptions> contentOptions,
        IOptionsMonitor<OtlpScrubberOptions> scrubberOptions)
    {
        _contentOptions = contentOptions;
        _scrubberOptions = scrubberOptions;
    }

    public void Scrub(ExportLogsServiceRequest req)
    {
        var chars = _contentOptions.CurrentValue.ContentPreviewChars;
        var secrets = _scrubberOptions.CurrentValue.SecretSubstrings;

        foreach (var rl in req.ResourceLogs)
        {
            foreach (var sl in rl.ScopeLogs)
            {
                foreach (var record in sl.LogRecords)
                {
                    ScrubAttributes(record.Attributes, chars, secrets);
                }
            }
        }
    }

    public void Scrub(ExportTraceServiceRequest req)
    {
        var chars = _contentOptions.CurrentValue.ContentPreviewChars;
        var secrets = _scrubberOptions.CurrentValue.SecretSubstrings;

        foreach (var rs in req.ResourceSpans)
        {
            foreach (var ss in rs.ScopeSpans)
            {
                foreach (var span in ss.Spans)
                {
                    ScrubAttributes(span.Attributes, chars, secrets);
                    foreach (var ev in span.Events)
                    {
                        ScrubAttributes(ev.Attributes, chars, secrets);
                    }
                }
            }
        }
    }

    private static void ScrubAttributes(
        RepeatedField<KeyValue> attributes,
        int contentPreviewChars,
        IReadOnlyList<string> secretSubstrings)
    {
        for (var i = attributes.Count - 1; i >= 0; i--)
        {
            var kv = attributes[i];
            if (kv.Key == ContentPreviewAttributeKey)
            {
                if (contentPreviewChars == 0)
                {
                    attributes.RemoveAt(i);
                    continue;
                }

                if (contentPreviewChars == -1)
                {
                    // Sentinel: no truncation. Fall through to secret-key check.
                }
                else if (kv.Value?.ValueCase == AnyValue.ValueOneofCase.StringValue)
                {
                    kv.Value.StringValue = TruncateWithEllipsis(kv.Value.StringValue, contentPreviewChars);
                }
            }

            if (IsSecretKey(kv.Key, secretSubstrings))
            {
                kv.Value ??= new AnyValue();
                // Assigning to StringValue clears the other oneof branches.
                kv.Value.StringValue = RedactedValue;
            }
        }
    }

    private static string TruncateWithEllipsis(string text, int chars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= chars)
        {
            return text;
        }
        return text[..chars] + "\u2026";
    }

    private static bool IsSecretKey(string key, IReadOnlyList<string> substrings)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }
        for (var i = 0; i < substrings.Count; i++)
        {
            if (key.Contains(substrings[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
