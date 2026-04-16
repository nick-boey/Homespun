using System.Text.Json;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Default <see cref="ISessionEventIngestor"/> implementation.
///
/// <para>
/// Contract:
/// </para>
/// <list type="number">
///   <item>Append the raw A2A payload to the on-disk log via <see cref="IA2AEventStore"/> —
///     this assigns the monotonic <c>seq</c> and stable <c>eventId</c> and flushes to disk
///     before anything is broadcast.</item>
///   <item>Translate the A2A event to zero-or-more AG-UI events through
///     <see cref="IA2AToAGUITranslator"/>. Unknown variants fall back to a
///     <c>Custom { name: "raw" }</c> event so nothing is silently dropped.</item>
///   <item>Broadcast one <see cref="SessionEventEnvelope"/> per translated AG-UI event to the
///     session's SignalR group. All derived envelopes share the parent A2A event's
///     <c>seq</c> and <c>eventId</c>, matching the replay endpoint's behavior exactly —
///     that is how live and refresh produce value-equal envelope streams.</item>
/// </list>
///
/// <para>
/// Translation and broadcast failures MUST NOT reverse the append. If persistence succeeded
/// but broadcast failed, the next refresh will still serve the event from the log; that is
/// the append-before-broadcast guarantee in action.
/// </para>
/// </summary>
public sealed class SessionEventIngestor : ISessionEventIngestor
{
    private readonly IA2AEventStore _store;
    private readonly IA2AToAGUITranslator _translator;
    private readonly IHubContext<ClaudeCodeHub> _hub;
    private readonly ILogger<SessionEventIngestor> _logger;

    public SessionEventIngestor(
        IA2AEventStore store,
        IA2AToAGUITranslator translator,
        IHubContext<ClaudeCodeHub> hub,
        ILogger<SessionEventIngestor> logger)
    {
        _store = store;
        _translator = translator;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task IngestAsync(
        string projectId,
        string sessionId,
        string eventKind,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        // Step 1: persist — this assigns seq + eventId and flushes before any client sees it.
        var record = await _store.AppendAsync(projectId, sessionId, eventKind, payload, cancellationToken);

        // Step 2: translate. The translator is deliberately tolerant of unknown shapes and
        // never throws; a null parsed result is itself a "we could not parse this" signal and
        // gets wrapped in a raw Custom envelope so the client still receives something.
        var ctx = new TranslationContext(sessionId, RunId: sessionId);
        var parsed = A2AMessageParser.ParseSseEvent(eventKind, payload.GetRawText());
        IEnumerable<AGUIBaseEvent> aguiEvents;
        if (parsed is null)
        {
            aguiEvents = new[]
            {
                AGUIEventFactory.CreateCustomEvent(
                    AGUICustomEventName.Raw,
                    new { original = payload }),
            };
        }
        else
        {
            aguiEvents = _translator.Translate(parsed, ctx);
        }

        // Step 3: broadcast. One envelope per translated AG-UI event, all sharing the parent
        // A2A event's seq and eventId so live and replay produce identical envelope streams.
        try
        {
            foreach (var agui in aguiEvents)
            {
                var envelope = new SessionEventEnvelope(
                    Seq: record.Seq,
                    SessionId: record.SessionId,
                    EventId: record.EventId,
                    Event: agui);
                await _hub.BroadcastSessionEvent(sessionId, envelope);
            }
        }
        catch (Exception ex)
        {
            // Broadcast failure does not roll back the append — future replay requests will
            // still serve this event from the log. Log and swallow so a transient hub
            // problem doesn't tear down the SSE consumer.
            _logger.LogWarning(ex,
                "Failed to broadcast SessionEventEnvelope seq={Seq} eventId={EventId} for session {SessionId}",
                record.Seq, record.EventId, sessionId);
        }
    }
}
