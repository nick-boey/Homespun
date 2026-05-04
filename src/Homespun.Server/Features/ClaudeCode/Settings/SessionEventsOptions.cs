namespace Homespun.Features.ClaudeCode.Settings;

/// <summary>
/// Configuration for the session-events replay endpoint
/// (<c>GET /api/sessions/{id}/events</c>).
///
/// <para>
/// Bound from the <c>SessionEvents</c> section of <c>appsettings.json</c>.
/// </para>
/// </summary>
public sealed class SessionEventsOptions
{
    public const string SectionName = "SessionEvents";

    /// <summary>
    /// Default replay mode when the client does not pass <c>?mode=</c>.
    /// The client may always override per-request by sending <c>mode=full</c>
    /// (or <c>mode=incremental</c>).
    /// </summary>
    public SessionEventsReplayMode ReplayMode { get; set; } = SessionEventsReplayMode.Incremental;

    /// <summary>
    /// Bounded timeout (in seconds) for the initial-message dispatch on
    /// <c>POST /api/sessions</c>. The controller awaits the dispatch up to
    /// this limit; on timeout the response is <c>202 Accepted</c> with the
    /// session id so the client can subscribe to the SignalR stream for the
    /// eventual result. Default 30s.
    /// </summary>
    public int DispatchTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Replay mode for the session-events endpoint.
/// </summary>
public enum SessionEventsReplayMode
{
    /// <summary>Return only events with <c>seq &gt; since</c>.</summary>
    Incremental,

    /// <summary>Return every event from seq 1, ignoring <c>since</c>.</summary>
    Full,
}
