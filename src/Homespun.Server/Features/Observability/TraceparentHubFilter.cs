using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Observability;

/// <summary>
/// SignalR <see cref="IHubFilter"/> that extracts the W3C <c>traceparent</c>
/// the client passes as the first argument to every hub method and starts a
/// server-side <see cref="Activity"/> parented to the client's span.
///
/// <para>
/// Convention: every client-facing hub method signature begins with
/// <c>string traceparent</c>. The client's <c>traceInvoke</c> helper
/// (<c>src/Homespun.Web/src/lib/signalr/trace.ts</c>) prepends it as arg0.
/// The filter is the single place the server pulls that value off the wire;
/// hub method bodies themselves ignore it.
/// </para>
///
/// <para>
/// Why a custom filter instead of
/// <c>AddSource("Microsoft.AspNetCore.SignalR.Server")</c>: the native source
/// records a span, but it has no hook for injecting a parent context from an
/// application-level argument. WebSocket transports do not expose per-message
/// headers to the client, so the traceparent has to travel as an argument —
/// and the filter is the only layer that gets to see those arguments with an
/// already-constructed parent context ready to attach to its activity. The
/// native source is explicitly disabled in
/// <c>Homespun.ServiceDefaults/Extensions.cs</c> to avoid double-emission.
/// </para>
/// </summary>
public sealed class TraceparentHubFilter : IHubFilter
{
    internal const string ActivitySourceName = HomespunActivitySources.Signalr;

    private static readonly ActivitySource Source = HomespunActivitySources.SignalrSource;

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var args = invocationContext.HubMethodArguments;
        var hubName = invocationContext.Hub.GetType().Name;
        var methodName = invocationContext.HubMethodName;
        var spanName = $"SignalR.{hubName}/{methodName}";

        // Parse the traceparent when the first argument looks like one. A
        // missing or malformed value is not an error — we just start a new
        // root activity so the server span still exists; downstream OTLP
        // consumers treat these as orphan spans and the call still completes.
        ActivityContext parentContext = default;
        var hasParent = false;
        if (args.Count > 0 && args[0] is string traceparent && !string.IsNullOrEmpty(traceparent)
            && ActivityContext.TryParse(traceparent, traceState: null, out parentContext))
        {
            hasParent = true;
        }

        using var activity = hasParent
            ? Source.StartActivity(spanName, ActivityKind.Server, parentContext)
            : Source.StartActivity(spanName, ActivityKind.Server);

        // Enrich with session id when the second argument is a string. The
        // hub convention puts sessionId right after the traceparent for most
        // methods; for the handful that don't (GetAllSessions, CheckCloneState,
        // StartSessionWithTermination) the tag is simply omitted.
        if (activity is not null && args.Count > 1 && args[1] is string sessionIdArg)
        {
            activity.SetTag("homespun.session.id", sessionIdArg);
        }

        if (activity is not null)
        {
            activity.SetTag("homespun.signalr.hub", hubName);
            activity.SetTag("homespun.signalr.method", methodName);
        }

        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddException(ex);
            }
            throw;
        }
    }
}
