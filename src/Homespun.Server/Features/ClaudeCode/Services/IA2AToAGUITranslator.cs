using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Context carrying per-turn identifiers the translator needs to emit canonical AG-UI
/// events (<c>RunStarted</c>, <c>RunFinished</c>, etc.).
/// </summary>
/// <param name="SessionId">The session id — used as the AG-UI <c>threadId</c>.</param>
/// <param name="RunId">The current run id — used as the AG-UI <c>runId</c>.</param>
public sealed record TranslationContext(string SessionId, string RunId);

/// <summary>
/// Pure translator from A2A events to AG-UI events. No I/O, no dependencies.
///
/// <para>
/// The translator is used for both the live broadcast path and the replay path so the
/// client observes the same envelope stream regardless of how the event arrived. A single
/// A2A event may fan out to zero or more AG-UI events (e.g. an A2A <c>Message</c> with
/// two content blocks produces multiple AG-UI events in order).
/// </para>
///
/// <para>
/// Unknown or unparsable A2A variants MUST NOT throw — they emit an AG-UI
/// <c>Custom { name: "raw", value: { original: ... } }</c> event so clients can preserve
/// and log them.
/// </para>
/// </summary>
public interface IA2AToAGUITranslator
{
    /// <summary>
    /// Translate an A2A event to zero or more AG-UI events.
    /// </summary>
    IEnumerable<AGUIBaseEvent> Translate(ParsedA2AEvent a2a, TranslationContext ctx);
}
