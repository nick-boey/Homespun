using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Logging;

/// <summary>
/// Source-generated log sites for the SSE → A2A → AG-UI ingestion pipeline.
///
/// <para>
/// Attribute keys follow the OpenTelemetry semantic-conventions naming spec
/// (lowercase, dot-delimited namespaces) so Seq queries can share predicates
/// across spans, logs, and tiers (worker / server / web). The worker
/// (<c>a2aEmitDebug</c>) and web (<c>log-envelope-rx</c>) emit identical keys
/// for the same trace.
/// </para>
///
/// <para>
/// <c>EventName</c> maps to <see cref="LogRecord.EventName"/> and is surfaced
/// as the semconv <c>event.name</c> attribute by modern OTel .NET exporters,
/// so the event identifier never needs to live as a message-prefix substring.
/// W3C <c>traceparent</c> is auto-populated on the log record by the OTel
/// logs bridge from <see cref="System.Diagnostics.Activity.Current"/>; do not
/// capture it as a structured property.
/// </para>
/// </summary>
internal static partial class SessionPipelineLog
{
    [LoggerMessage(
        EventId = 2001,
        EventName = "a2a.rx",
        Level = LogLevel.Information,
        Message = "a2a.rx kind={homespun.a2a.kind} seq={homespun.seq}")]
    public static partial void A2ARx(
        this ILogger logger,
        [TagName("homespun.a2a.kind")] string kind,
        [TagName("homespun.seq")] long seq,
        [TagName("homespun.session.id")] string sessionId,
        [TagName("homespun.body")] string body);

    [LoggerMessage(
        EventId = 2002,
        EventName = "agui.tx",
        Level = LogLevel.Information,
        Message = "agui.tx seq={homespun.seq}")]
    public static partial void AGUITx(
        this ILogger logger,
        [TagName("homespun.seq")] long seq,
        [TagName("homespun.session.id")] string sessionId,
        [TagName("homespun.body")] string body);
}
