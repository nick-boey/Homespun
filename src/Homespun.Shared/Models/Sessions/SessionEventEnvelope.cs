using System.Text.Json.Serialization;

namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Wire envelope broadcast to clients via SignalR and returned from the replay endpoint
/// <c>GET /api/sessions/{id}/events</c>.
///
/// <para>
/// Every AG-UI event that reaches a client — live or on replay — is wrapped in this
/// envelope. The envelope carries the metadata the client needs to deduplicate, order,
/// and resume: a per-session monotonic <see cref="Seq"/>, a stable <see cref="EventId"/>
/// that survives replay, and the AG-UI event payload itself.
/// </para>
///
/// <para>
/// Live broadcast and replay produce envelopes that compare equal by value for the same
/// underlying A2A event — same <see cref="Seq"/>, same <see cref="EventId"/>, same
/// <see cref="Event"/> payload. That property is what makes the client reducer identical
/// for live and refresh paths.
/// </para>
/// </summary>
/// <param name="Seq">
/// Per-session monotonic sequence number starting at 1. Clients persist the highest-observed
/// seq as <c>lastSeenSeq</c> and pass it on the replay endpoint as <c>?since=</c> to resume
/// from the right point. Seq is assigned by <c>A2AEventStore</c> at append time and is
/// strictly monotonic within a session; it is not ordered across sessions.
/// </param>
/// <param name="SessionId">The Claude Code session this envelope belongs to.</param>
/// <param name="EventId">
/// Stable UUID assigned to the underlying A2A event when it is ingested by the server.
/// The same id appears on every broadcast and every replay of that event, so clients can
/// deduplicate by this value alone — independent of whether live and replay streams overlap.
/// </param>
/// <param name="Event">The AG-UI event payload (discriminated by <see cref="AGUIBaseEvent.Type"/>).</param>
/// <param name="Traceparent">
/// Optional W3C <c>traceparent</c> captured from the server's <c>Activity.Current</c>
/// at broadcast time. Clients use it to parent the reducer-apply span to the server's
/// ingest span so live and refresh produce the same trace tree in Seq. Null when no
/// activity is in flight (e.g. replay responses rebuilt from disk outside a request
/// scope).
/// </param>
public sealed record SessionEventEnvelope(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("event")] AGUIBaseEvent Event,
    [property: JsonPropertyName("traceparent")] string? Traceparent = null);
