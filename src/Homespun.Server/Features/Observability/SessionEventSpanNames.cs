namespace Homespun.Features.Observability;

/// <summary>
/// Span-name constants for the session-event pipeline. Emitted by
/// <see cref="HomespunActivitySources.SessionPipelineSource"/> (server-ingest
/// path) and <see cref="HomespunActivitySources.SignalrSource"/> (hub
/// lifecycle).
/// </summary>
public static class SessionEventSpanNames
{
    public const string Ingest = "homespun.session.ingest";
    public const string Translate = "homespun.agui.translate";
    public const string SignalrConnect = "homespun.signalr.connect";
    public const string SignalrJoin = "homespun.signalr.join";
    public const string SignalrLeave = "homespun.signalr.leave";
}

/// <summary>
/// Span-event names attached to ingest spans for each hop in the pipeline.
/// </summary>
public static class SessionEventSpanEvents
{
    public const string SseRx = "sse.rx";
    public const string IngestAppend = "ingest.append";
    public const string SignalrTx = "signalr.tx";
    public const string Connected = "connected";
    public const string Disconnected = "disconnected";
}
