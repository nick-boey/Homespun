using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Observability;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly SessionEventLogOptions _sessionEventLogOptions;

    public SessionEventIngestor(
        IA2AEventStore store,
        IA2AToAGUITranslator translator,
        IHubContext<ClaudeCodeHub> hub,
        ILogger<SessionEventIngestor> logger,
        IServiceProvider serviceProvider,
        IOptions<SessionEventLogOptions>? sessionEventLogOptions = null)
    {
        _store = store;
        _translator = translator;
        _hub = hub;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _sessionEventLogOptions = sessionEventLogOptions?.Value ?? new SessionEventLogOptions();
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

        // Step 3: broadcast. One envelope per translated AG-UI event, all sharing the parent
        // A2A event's seq and eventId so live and replay produce identical envelope streams.
        ExtractParentCorrelation(
            parsed,
            out var parentTaskId,
            out var parentMessageId,
            out var parentArtifactId,
            out var parentStatusTimestamp);

        try
        {
            foreach (var agui in aguiEvents)
            {
                // server.agui.translate hop: one per AG-UI envelope produced from the parent A2A event.
                SessionEventLog.LogAGUIHop(
                    _logger,
                    _sessionEventLogOptions,
                    hop: SessionEventHops.ServerAguiTranslate,
                    sessionId: sessionId,
                    agui: agui,
                    seq: record.Seq,
                    eventId: record.EventId,
                    parentMessageId: parentMessageId,
                    parentArtifactId: parentArtifactId,
                    parentStatusTimestamp: parentStatusTimestamp,
                    parentTaskId: parentTaskId,
                    a2aKind: eventKind);

                var envelope = new SessionEventEnvelope(
                    Seq: record.Seq,
                    SessionId: record.SessionId,
                    EventId: record.EventId,
                    Event: agui);
                await _hub.BroadcastSessionEvent(sessionId, envelope);

                // server.signalr.tx hop: emitted after the SignalR dispatch returns.
                SessionEventLog.LogAGUIHop(
                    _logger,
                    _sessionEventLogOptions,
                    hop: SessionEventHops.ServerSignalrTx,
                    sessionId: sessionId,
                    agui: agui,
                    seq: record.Seq,
                    eventId: record.EventId,
                    parentMessageId: parentMessageId,
                    parentArtifactId: parentArtifactId,
                    parentStatusTimestamp: parentStatusTimestamp,
                    parentTaskId: parentTaskId,
                    a2aKind: eventKind);
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
    }
}
