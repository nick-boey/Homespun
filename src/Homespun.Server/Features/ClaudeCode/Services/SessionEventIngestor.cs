using System.Diagnostics;
using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

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
/// Every ingestion emits a <c>homespun.session.ingest</c> span on
/// <see cref="HomespunActivitySources.SessionPipelineSource"/> with span events
/// <c>sse.rx</c>, <c>ingest.append</c>, and <c>signalr.tx</c> in order. A child
/// <c>homespun.agui.translate</c> span covers the translator call. Translation
/// and broadcast failures MUST NOT reverse the append — if persistence succeeded
/// but broadcast failed the next refresh will still serve the event from the log.
/// </para>
/// </summary>
public sealed class SessionEventIngestor : ISessionEventIngestor
{
    private readonly IA2AEventStore _store;
    private readonly IA2AToAGUITranslator _translator;
    private readonly IHubContext<ClaudeCodeHub> _hub;
    private readonly ILogger<SessionEventIngestor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IContentPreviewGate _previewGate;
    private readonly IOptionsMonitor<SessionDebugLoggingOptions> _debugOptions;

    public SessionEventIngestor(
        IA2AEventStore store,
        IA2AToAGUITranslator translator,
        IHubContext<ClaudeCodeHub> hub,
        ILogger<SessionEventIngestor> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<SessionDebugLoggingOptions> debugOptions,
        IContentPreviewGate? previewGate = null)
    {
        _store = store;
        _translator = translator;
        _hub = hub;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _debugOptions = debugOptions;
        _previewGate = previewGate ?? new NoopContentPreviewGate();
    }

    /// <inheritdoc />
    public async Task IngestAsync(
        string projectId,
        string sessionId,
        string eventKind,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        using var ingestSpan = HomespunActivitySources.SessionPipelineSource.StartActivity("homespun.session.ingest", ActivityKind.Consumer);

        ingestSpan?.SetTag("homespun.session.id", sessionId);
        ingestSpan?.SetTag("homespun.a2a.kind", eventKind);

        ingestSpan?.AddEvent(new ActivityEvent(SessionEventSpanEvents.SseRx));

        // Step 1: persist — this assigns seq + eventId and flushes before any client sees it.
        var record = await _store.AppendAsync(projectId, sessionId, eventKind, payload, cancellationToken);

        ingestSpan?.SetTag("homespun.seq", record.Seq);
        ingestSpan?.SetTag("homespun.event.id", record.EventId);
        ingestSpan?.AddEvent(new ActivityEvent(SessionEventSpanEvents.IngestAppend));

        if (_debugOptions.CurrentValue.FullMessages)
        {
            _logger.LogInformation(
                "a2a.rx kind={Kind} seq={Seq} body={Body}",
                eventKind,
                record.Seq,
                payload.GetRawText());
        }

        // Step 2: translate. The translator is deliberately tolerant of unknown shapes and
        // never throws; a null parsed result is itself a "we could not parse this" signal and
        // gets wrapped in a raw Custom envelope so the client still receives something.
        var ctx = new TranslationContext(sessionId, RunId: sessionId, EventId: record.EventId);
        var parsed = A2AMessageParser.ParseSseEvent(eventKind, payload.GetRawText());

        ExtractParentCorrelation(
            parsed,
            out var parentTaskId,
            out var parentMessageId,
            out var parentArtifactId,
            out _);

        if (parentTaskId is not null) ingestSpan?.SetTag("homespun.task.id", parentTaskId);
        if (parentMessageId is not null) ingestSpan?.SetTag("homespun.message.id", parentMessageId);
        if (parentArtifactId is not null) ingestSpan?.SetTag("homespun.artifact.id", parentArtifactId);

        var preview = _previewGate.Gate(ExtractPreview(parsed, payload.GetRawText()));
        if (preview is not null)
        {
            ingestSpan?.SetTag("homespun.content.preview", preview);
        }

        IEnumerable<AGUIBaseEvent> aguiEvents;
        using (var translateSpan = HomespunActivitySources.SessionPipelineSource.StartActivity("homespun.agui.translate", ActivityKind.Internal))
        {
            translateSpan?.SetTag("homespun.a2a.kind", eventKind);
            translateSpan?.SetTag("homespun.session.id", sessionId);

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
                aguiEvents = _translator.Translate(parsed, ctx).ToList();

                // Tool-use dispatch: for A2A Message events carrying tool_use blocks,
                // route workflow_signal and Write (plan-capture) side effects through
                // ToolInteractionService. AskUserQuestion/ExitPlanMode are intercepted by
                // the worker's canUseTool and surface as input-required status-updates, so
                // they fall outside this tap.
                if (parsed is ParsedAgentMessage agentMsg)
                {
                    await DispatchToolUsesAsync(sessionId, agentMsg, cancellationToken);
                }
            }
        }

        // Step 3: broadcast. One envelope per translated AG-UI event, all sharing the parent
        // A2A event's seq and eventId so live and replay produce identical envelope streams.
        var fullMessages = _debugOptions.CurrentValue.FullMessages;
        try
        {
            foreach (var agui in aguiEvents)
            {
                // Capture the W3C traceparent for the current activity (the ingest span,
                // parented to the worker SSE HTTP span) so the client can parent its
                // reducer-apply span to this broadcast. On replay this will be null and
                // the client falls back to its own root span.
                var envelope = new SessionEventEnvelope(
                    Seq: record.Seq,
                    SessionId: record.SessionId,
                    EventId: record.EventId,
                    Event: agui,
                    Traceparent: FormatCurrentTraceparent());

                if (fullMessages)
                {
                    _logger.LogInformation(
                        "agui.tx seq={Seq} sessionId={SessionId} traceparent={Traceparent} body={Body}",
                        envelope.Seq,
                        envelope.SessionId,
                        envelope.Traceparent,
                        JsonSerializer.Serialize(envelope));
                }

                await _hub.BroadcastSessionEvent(sessionId, envelope);
            }

            ingestSpan?.AddEvent(new ActivityEvent(SessionEventSpanEvents.SignalrTx));
        }
        catch (Exception ex)
        {
            // Broadcast failure does not roll back the append — future replay requests will
            // still serve this event from the log. Log and swallow so a transient hub
            // problem doesn't tear down the SSE consumer.
            ingestSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
            ingestSpan?.AddException(ex);
            _logger.LogWarning(ex,
                "Failed to broadcast SessionEventEnvelope seq={Seq} eventId={EventId} for session {SessionId}",
                record.Seq, record.EventId, sessionId);
        }
    }

    private static void ExtractParentCorrelation(
        ParsedA2AEvent? parsed,
        out string? taskId,
        out string? messageId,
        out string? artifactId,
        out string? statusTimestamp)
    {
        taskId = null;
        messageId = null;
        artifactId = null;
        statusTimestamp = null;

        switch (parsed)
        {
            case ParsedAgentMessage pm:
                messageId = pm.Message.MessageId;
                taskId = pm.Message.TaskId;
                break;
            case ParsedAgentTask pt:
                taskId = pt.Task.Id;
                break;
            case ParsedTaskStatusUpdateEvent ps:
                taskId = ps.StatusUpdate.TaskId;
                statusTimestamp = ps.StatusUpdate.Status.Timestamp.ToString("O");
                break;
            case ParsedTaskArtifactUpdateEvent pa:
                taskId = pa.ArtifactUpdate.TaskId;
                artifactId = pa.ArtifactUpdate.Artifact?.ArtifactId;
                break;
        }
    }

    private static string? ExtractPreview(ParsedA2AEvent? parsed, string? rawJson)
    {
        if (parsed is ParsedAgentMessage pm && pm.Message.Parts is not null)
        {
            foreach (var part in pm.Message.Parts)
            {
                if (part is TextPart tp && !string.IsNullOrEmpty(tp.Text))
                {
                    return tp.Text;
                }
            }
        }

        return rawJson;
    }

    /// <summary>
    /// Walks an agent message's tool_use data parts and dispatches side-effect handlers
    /// (<c>Write</c> → <see cref="IToolInteractionService.TryCaptureWrittenPlanContent"/>).
    /// Dependencies are resolved lazily per-call to keep the ingestor's constructor thin
    /// and side-step any circularity between ingestor and tool-interaction graph.
    /// </summary>
    private async Task DispatchToolUsesAsync(
        string sessionId,
        ParsedAgentMessage agentMsg,
        CancellationToken cancellationToken)
    {
        var parts = agentMsg.Message.Parts;
        if (parts is null || parts.Count == 0) return;

        IToolInteractionService? toolInteraction = null;
        IClaudeSessionStore? sessionStore = null;
        ClaudeSession? session = null;

        foreach (var part in parts)
        {
            if (part is not DataPart dataPart) continue;
            if (dataPart.Metadata.GetMetadataString("kind") != "tool_use") continue;

            var toolName = dataPart.GetDataString("toolName");
            if (string.IsNullOrEmpty(toolName)) continue;

            var inputElement = dataPart.GetDataElement("input");
            var inputJson = inputElement.HasValue ? inputElement.Value.GetRawText() : null;

            toolInteraction ??= _serviceProvider.GetService(typeof(IToolInteractionService)) as IToolInteractionService;
            if (toolInteraction is null)
            {
                _logger.LogDebug("No IToolInteractionService registered; skipping tool-use dispatch for session {SessionId}", sessionId);
                return;
            }

            if (session is null)
            {
                sessionStore ??= _serviceProvider.GetService(typeof(IClaudeSessionStore)) as IClaudeSessionStore;
                session = sessionStore?.GetById(sessionId);
                if (session is null)
                {
                    _logger.LogDebug("Session {SessionId} not found in store; tool-use dispatch skipped", sessionId);
                    return;
                }
            }

            try
            {
                if (toolName.Equals("Write", StringComparison.OrdinalIgnoreCase))
                {
                    toolInteraction.TryCaptureWrittenPlanContent(session, inputJson);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool-use dispatch failed for {ToolName} in session {SessionId}", toolName, sessionId);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Formats <see cref="Activity.Current"/> as a W3C <c>traceparent</c>
    /// string (<c>00-&lt;traceId&gt;-&lt;spanId&gt;-&lt;flags&gt;</c>) so a
    /// client receiving the envelope can parent its span to this broadcast.
    /// Returns <c>null</c> when no activity is in flight — replay responses
    /// rebuilt from disk fall into that bucket and the client treats a null
    /// traceparent as "start a new trace root".
    /// </summary>
    private static string? FormatCurrentTraceparent()
    {
        var activity = Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
        {
            return null;
        }

        var flags = (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        return $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{flags}";
    }

    private sealed class NoopContentPreviewGate : IContentPreviewGate
    {
        public string? Gate(string? text) => null;
    }
}
