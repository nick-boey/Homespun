namespace Homespun.Features.Observability;

/// <summary>
/// Toggles full-body debug logging across the session pipeline. When
/// <see cref="FullMessages"/> is true, <c>SessionEventIngestor</c>,
/// <c>A2AToAGUITranslator</c>, the SignalR broadcast site, and the
/// <c>SessionEventsController</c> replay endpoint emit full A2A / AG-UI /
/// envelope bodies as OTel log entries.
///
/// <para>Bound from the <c>HOMESPUN_DEBUG_FULL_MESSAGES</c> environment
/// variable (<c>"true"</c> → enabled). The flag defaults to off in every
/// launch profile and is read at process start.</para>
/// </summary>
public sealed class SessionDebugLoggingOptions
{
    public const string EnvVarName = "HOMESPUN_DEBUG_FULL_MESSAGES";

    /// <summary>
    /// When true, the session pipeline emits full-body log entries at
    /// <c>a2a.rx</c>, <c>agui.translate</c>, <c>agui.tx</c>, and the replay
    /// endpoint. Defaults to false.
    /// </summary>
    public bool FullMessages { get; set; } = false;
}
