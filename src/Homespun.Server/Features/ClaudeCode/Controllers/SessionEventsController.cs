using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.ClaudeCode.Settings;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Controllers;

/// <summary>
/// Replay endpoint for session events. Returns <see cref="SessionEventEnvelope"/>s in
/// ascending <c>seq</c> order — the same shape the live SignalR path broadcasts — so the
/// client can feed live and replay envelopes through the same reducer.
/// </summary>
[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public sealed class SessionEventsController(
    IA2AEventStore eventStore,
    IA2AToAGUITranslator translator,
    IOptions<SessionEventsOptions> options) : ControllerBase
{
    /// <summary>
    /// Get AG-UI envelopes for a session.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="since">
    /// Return envelopes with <c>seq &gt; since</c>. Ignored when <paramref name="mode"/>
    /// resolves to <see cref="SessionEventsReplayMode.Full"/>.
    /// </param>
    /// <param name="mode">
    /// <c>"incremental"</c> or <c>"full"</c>. When omitted, defaults to the
    /// <c>SessionEvents:ReplayMode</c> server configuration.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{sessionId}/events")]
    [ProducesResponseType<IReadOnlyList<SessionEventEnvelope>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SessionEventEnvelope>>> GetEvents(
        string sessionId,
        [FromQuery] long? since,
        [FromQuery] string? mode,
        CancellationToken ct)
    {
        var resolvedMode = ResolveMode(mode, options.Value.ReplayMode);
        var effectiveSince = resolvedMode == SessionEventsReplayMode.Full ? (long?)null : since;

        var records = await eventStore.ReadAsync(sessionId, effectiveSince, ct);
        if (records is null)
        {
            return NotFound();
        }

        var envelopes = new List<SessionEventEnvelope>();
        var ctx = new TranslationContext(sessionId, RunId: sessionId);

        foreach (var record in records)
        {
            var parsed = A2AMessageParser.ParseSseEvent(record.EventKind, record.Payload.GetRawText());
            if (parsed is null)
            {
                // Unparsable on-disk record — surface as raw so the client doesn't silently drop it.
                envelopes.Add(new SessionEventEnvelope(
                    Seq: record.Seq,
                    SessionId: record.SessionId,
                    EventId: record.EventId,
                    Event: AGUIEventFactory.CreateCustomEvent(
                        AGUICustomEventName.Raw,
                        new { original = record.Payload })));
                continue;
            }

            foreach (var agui in translator.Translate(parsed, ctx))
            {
                envelopes.Add(new SessionEventEnvelope(
                    Seq: record.Seq,
                    SessionId: record.SessionId,
                    EventId: record.EventId,
                    Event: agui));
            }
        }

        return Ok(envelopes);
    }

    private static SessionEventsReplayMode ResolveMode(string? requested, SessionEventsReplayMode serverDefault)
    {
        if (string.IsNullOrWhiteSpace(requested)) return serverDefault;
        return requested.Equals("full", StringComparison.OrdinalIgnoreCase)
            ? SessionEventsReplayMode.Full
            : SessionEventsReplayMode.Incremental;
    }
}
