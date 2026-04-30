using Homespun.Features.ClaudeCode.Settings;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Logging;

/// <summary>
/// Source-generated log sites for the session-events replay endpoint.
/// Attribute keys follow OpenTelemetry semconv naming (see
/// <see cref="SessionPipelineLog"/> for the full rationale).
///
/// <para>
/// Replay-path log entries also carry the ambient <c>homespun.replay=true</c>
/// scope set by the controller so Seq users can filter duplicates with
/// <c>homespun.replay is null</c>.
/// </para>
/// </summary>
internal static partial class ReplayLog
{
    [LoggerMessage(
        EventId = 2004,
        EventName = "agui.replay",
        Level = LogLevel.Information,
        Message = "agui.replay seq={homespun.seq} type={homespun.agui.type}")]
    public static partial void AGUIReplay(
        this ILogger logger,
        [TagName("homespun.seq")] long seq,
        [TagName("homespun.session.id")] string sessionId,
        [TagName("homespun.agui.type")] string type,
        [TagName("homespun.body")] string body);

    [LoggerMessage(
        EventId = 2005,
        EventName = "agui.replay.batch",
        Level = LogLevel.Information,
        Message = "agui.replay.batch mode={homespun.replay.mode} count={homespun.replay.count}")]
    public static partial void AGUIReplayBatch(
        this ILogger logger,
        [TagName("homespun.session.id")] string sessionId,
        [TagName("homespun.replay.mode")] SessionEventsReplayMode mode,
        [TagName("homespun.replay.since")] long? since,
        [TagName("homespun.replay.count")] int count);
}
