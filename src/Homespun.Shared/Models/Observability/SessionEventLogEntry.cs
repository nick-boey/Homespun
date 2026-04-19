using System.Text.Json.Serialization;

namespace Homespun.Shared.Models.Observability;

/// <summary>
/// Shared structured log-entry shape emitted at each of the six session event
/// pipeline hops (worker.a2a.emit → client.reducer.apply). All correlation
/// fields are top-level so Seq / the Aspire dashboard surface them as
/// addressable OTLP log attributes without nested extraction.
/// </summary>
public sealed record SessionEventLogEntry
{
    [JsonPropertyName("Timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("Level")]
    public required string Level { get; init; }

    [JsonPropertyName("Message")]
    public required string Message { get; init; }

    [JsonPropertyName("SourceContext")]
    public required string SourceContext { get; init; }

    [JsonPropertyName("Component")]
    public required string Component { get; init; }

    [JsonPropertyName("Hop")]
    public required string Hop { get; init; }

    [JsonPropertyName("SessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("TaskId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskId { get; init; }

    [JsonPropertyName("MessageId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageId { get; init; }

    [JsonPropertyName("ArtifactId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactId { get; init; }

    [JsonPropertyName("StatusTimestamp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusTimestamp { get; init; }

    [JsonPropertyName("EventId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; init; }

    [JsonPropertyName("Seq"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Seq { get; init; }

    [JsonPropertyName("A2AKind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? A2AKind { get; init; }

    [JsonPropertyName("AGUIType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AGUIType { get; init; }

    [JsonPropertyName("AGUICustomName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AGUICustomName { get; init; }

    [JsonPropertyName("ContentPreview"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentPreview { get; init; }
}

/// <summary>
/// Enumerates the six defined pipeline hops at which a
/// <see cref="SessionEventLogEntry"/> is emitted.
/// </summary>
public static class SessionEventHops
{
    public const string WorkerA2AEmit = "worker.a2a.emit";
    public const string ServerSseRx = "server.sse.rx";
    public const string ServerIngestAppend = "server.ingest.append";
    public const string ServerAguiTranslate = "server.agui.translate";
    public const string ServerSignalrTx = "server.signalr.tx";
    public const string ClientSignalrRx = "client.signalr.rx";
    public const string ClientReducerApply = "client.reducer.apply";

    // Hub lifecycle diagnostics. Not part of the event fan-out but necessary to
    // distinguish "client never connected" / "client never joined group" from
    // "server broadcast never reached client".
    public const string ServerHubConnected = "server.hub.connected";
    public const string ServerHubDisconnected = "server.hub.disconnected";
    public const string ServerHubJoin = "server.hub.join";
    public const string ServerHubLeave = "server.hub.leave";
    public const string ClientSignalrConnect = "client.signalr.connect";
    public const string ClientSignalrDisconnect = "client.signalr.disconnect";
    public const string ClientSignalrReconnecting = "client.signalr.reconnecting";
    public const string ClientSignalrReconnected = "client.signalr.reconnected";
    public const string ClientSignalrJoin = "client.signalr.join";
    public const string ClientSignalrJoinError = "client.signalr.join.error";

    public static readonly IReadOnlyList<string> All =
    [
        WorkerA2AEmit,
        ServerSseRx,
        ServerIngestAppend,
        ServerAguiTranslate,
        ServerSignalrTx,
        ClientSignalrRx,
        ClientReducerApply,
        ServerHubConnected,
        ServerHubDisconnected,
        ServerHubJoin,
        ServerHubLeave,
        ClientSignalrConnect,
        ClientSignalrDisconnect,
        ClientSignalrReconnecting,
        ClientSignalrReconnected,
        ClientSignalrJoin,
        ClientSignalrJoinError,
    ];
}

/// <summary>
/// Component tag for the <see cref="SessionEventLogEntry.Component"/> field.
/// </summary>
public static class SessionEventComponents
{
    public const string Worker = "Worker";
    public const string Server = "Server";
    public const string Web = "Web";
}

/// <summary>
/// SourceContext tag for the <see cref="SessionEventLogEntry.SourceContext"/> field.
/// </summary>
public static class SessionEventSourceContexts
{
    public const string Server = "Homespun.SessionEvents";
    public const string Client = "Homespun.ClientSessionEvents";
    public const string Worker = "Worker";
}

[JsonSerializable(typeof(SessionEventLogEntry))]
public sealed partial class SessionEventLogEntryContext : JsonSerializerContext
{
}
