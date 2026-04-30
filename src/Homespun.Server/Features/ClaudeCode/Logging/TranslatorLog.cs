using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Logging;

/// <summary>
/// Source-generated log site for the A2A → AG-UI translator. Attribute keys
/// follow OpenTelemetry semconv naming (see <see cref="SessionPipelineLog"/>
/// for the full rationale).
/// </summary>
internal static partial class TranslatorLog
{
    [LoggerMessage(
        EventId = 2003,
        EventName = "agui.translate",
        Level = LogLevel.Information,
        Message = "agui.translate type={homespun.agui.type}")]
    public static partial void AGUITranslate(
        this ILogger logger,
        [TagName("homespun.agui.type")] string type,
        [TagName("homespun.session.id")] string sessionId,
        [TagName("homespun.body")] string body);
}
