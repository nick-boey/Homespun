using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Per-worker-session long-lived SSE consumer. Runs a background task that
/// reads the worker's <c>GET /api/sessions/{id}/events</c> endpoint for the
/// full life of the session, invokes <see cref="ISessionEventIngestor"/>
/// on every A2A event (so SignalR broadcasts never stop), and fans out
/// parsed <see cref="SdkMessage"/> values to per-turn subscribers.
///
/// <para>
/// This service exists to decouple the client-visible event stream from the
/// per-turn HTTP request cycle. The Claude Agent SDK emits
/// <c>SDKTaskNotificationMessage</c> / <c>SDKTaskStartedMessage</c> /
/// <c>SDKTaskUpdatedMessage</c> as background bash tasks settle — often
/// minutes after the <c>result</c> message that ends a turn. Before this
/// service, those events piled up in the worker's <c>OutputChannel</c> with
/// no consumer; now they flow through the long-lived reader and reach the
/// client live.
/// </para>
/// </summary>
public interface IPerSessionEventStream
{
    /// <summary>
    /// Starts the background reader for a worker session. Idempotent —
    /// second calls for the same <paramref name="homespunSessionId"/> are
    /// a no-op.
    /// </summary>
    Task StartAsync(
        string homespunSessionId,
        string workerUrl,
        string workerSessionId,
        string? projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes to SDK messages for a single turn. Returned enumerable
    /// completes after the next <see cref="SdkResultMessage"/> is yielded.
    /// Throws if no reader is running for <paramref name="homespunSessionId"/>.
    /// </summary>
    IAsyncEnumerable<SdkMessage> SubscribeTurnAsync(
        string homespunSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops the background reader and completes any pending subscriber.
    /// Safe to call for an unknown session id.
    /// </summary>
    Task StopAsync(string homespunSessionId);
}
