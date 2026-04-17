using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Observability;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Observability;

/// <summary>
/// Structured logging helper for the A2A → AG-UI session event pipeline.
///
/// <para>
/// Every entry is emitted as flat JSON to stdout so Loki's <c>| json</c> pipeline
/// stage returns correlation fields addressable by name. The source context is
/// fixed to <see cref="SessionEventSourceContexts.Server"/> for server-side hops
/// and <see cref="SessionEventSourceContexts.Client"/> for client-forwarded
/// entries routed through <c>POST /api/log/client</c>.
/// </para>
///
/// <para>
/// Emission is gated by <see cref="ILogger.IsEnabled"/> at <see cref="LogLevel.Information"/>
/// and by the per-hop <c>Enabled</c> flag in <see cref="SessionEventLogOptions.Hops"/>.
/// </para>
/// </summary>
public static class SessionEventLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns <paramref name="text"/> truncated to <paramref name="chars"/> characters
    /// followed by an ellipsis when longer than <paramref name="chars"/>. Returns
    /// <c>null</c> when <paramref name="chars"/> is zero or <paramref name="text"/> is
    /// null. Returns the original text when it is already within the budget.
    /// </summary>
    public static string? TruncatePreview(string? text, int chars)
    {
        if (chars <= 0 || text is null)
        {
            return null;
        }

        if (text.Length <= chars)
        {
            return text;
        }

        return text[..chars] + "\u2026";
    }

    /// <summary>
    /// Logs an A2A-level hop (<c>server.sse.rx</c>, <c>server.ingest.append</c>).
    /// The <paramref name="parsed"/> argument is optional — pass it when the caller
    /// has already parsed the SSE payload. <paramref name="rawJsonForPreview"/> is
    /// the raw SSE data; the helper extracts a short text-content preview from it
    /// when <see cref="SessionEventLogOptions.ContentPreviewChars"/> is non-zero.
    /// </summary>
    public static void LogA2AHop(
        ILogger logger,
        SessionEventLogOptions options,
        string hop,
        string sessionId,
        string a2aKind,
        ParsedA2AEvent? parsed,
        string? rawJsonForPreview = null,
        long? seq = null,
        string? eventId = null)
    {
        if (!ShouldLog(logger, options, hop))
        {
            return;
        }

        ExtractA2ACorrelation(parsed, out var taskId, out var messageId, out var artifactId, out var statusTimestamp);
        var preview = options.ContentPreviewChars > 0
            ? TruncatePreview(ExtractPreview(parsed, rawJsonForPreview), options.ContentPreviewChars)
            : null;

        var entry = new SessionEventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Level = "Information",
            Message = FormatMessage(hop, a2aKind: a2aKind, seq: seq, messageId: messageId),
            SourceContext = SessionEventSourceContexts.Server,
            Component = SessionEventComponents.Server,
            Hop = hop,
            SessionId = sessionId,
            TaskId = taskId,
            MessageId = messageId,
            ArtifactId = artifactId,
            StatusTimestamp = statusTimestamp,
            EventId = eventId,
            Seq = seq,
            A2AKind = a2aKind,
            ContentPreview = preview,
        };

        Emit(entry);
    }

    /// <summary>
    /// Logs an AG-UI-level hop (<c>server.agui.translate</c>, <c>server.signalr.tx</c>).
    /// Caller supplies the translated AG-UI event plus the parent A2A correlation
    /// IDs so every derived envelope line traces back to the same A2A event.
    /// </summary>
    public static void LogAGUIHop(
        ILogger logger,
        SessionEventLogOptions options,
        string hop,
        string sessionId,
        AGUIBaseEvent agui,
        long seq,
        string eventId,
        string? parentMessageId = null,
        string? parentArtifactId = null,
        string? parentStatusTimestamp = null,
        string? parentTaskId = null,
        string? a2aKind = null,
        string? contentPreview = null)
    {
        if (!ShouldLog(logger, options, hop))
        {
            return;
        }

        var customName = agui is CustomEvent custom ? custom.Name : null;
        var preview = options.ContentPreviewChars > 0
            ? TruncatePreview(contentPreview ?? ExtractAGUIPreview(agui), options.ContentPreviewChars)
            : null;

        var entry = new SessionEventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Level = "Information",
            Message = FormatMessage(hop, aguiType: agui.Type, customName: customName, seq: seq, messageId: parentMessageId),
            SourceContext = SessionEventSourceContexts.Server,
            Component = SessionEventComponents.Server,
            Hop = hop,
            SessionId = sessionId,
            TaskId = parentTaskId,
            MessageId = parentMessageId,
            ArtifactId = parentArtifactId,
            StatusTimestamp = parentStatusTimestamp,
            EventId = eventId,
            Seq = seq,
            A2AKind = a2aKind,
            AGUIType = agui.Type,
            AGUICustomName = customName,
            ContentPreview = preview,
        };

        Emit(entry);
    }

    /// <summary>
    /// Logs a hub-lifecycle hop (connect / disconnect / join / leave). These are
    /// diagnostic-only — they do not correspond to an A2A or AG-UI event — but
    /// they share the log schema so one LogQL query pinned to <c>SessionId</c>
    /// returns lifecycle + event hops together.
    /// </summary>
    public static void LogHubHop(
        ILogger logger,
        SessionEventLogOptions options,
        string hop,
        string sessionId,
        string? connectionId = null,
        string? detail = null)
    {
        if (!ShouldLog(logger, options, hop))
        {
            return;
        }

        var parts = new List<string> { hop };
        if (!string.IsNullOrEmpty(connectionId))
        {
            parts.Add($"conn={ShortId(connectionId!)}");
        }
        if (!string.IsNullOrEmpty(detail))
        {
            parts.Add(detail!);
        }

        var entry = new SessionEventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Level = "Information",
            Message = string.Join(' ', parts),
            SourceContext = SessionEventSourceContexts.Server,
            Component = SessionEventComponents.Server,
            Hop = hop,
            SessionId = sessionId,
            ContentPreview = connectionId is null ? null : $"connectionId={connectionId}",
        };

        Emit(entry);
    }

    /// <summary>
    /// Writes a prebuilt entry to the server-side log stream under
    /// <see cref="SessionEventSourceContexts.Client"/>. Used by
    /// <c>POST /api/log/client</c> to forward browser-originated entries.
    /// </summary>
    public static void LogClientHop(
        ILogger logger,
        SessionEventLogOptions options,
        SessionEventLogEntry clientEntry)
    {
        if (!ShouldLog(logger, options, clientEntry.Hop))
        {
            return;
        }

        Emit(clientEntry with
        {
            SourceContext = SessionEventSourceContexts.Client,
            Component = SessionEventComponents.Web,
        });
    }

    private static bool ShouldLog(ILogger logger, SessionEventLogOptions options, string hop)
    {
        // The per-hop Enabled flag is the sole gate for volume control; the
        // logger is accepted only as a hook for the call site's category context
        // and future log-level filtering. NullLogger.IsEnabled returns false,
        // so gating on it would silently suppress logs in test doubles.
        _ = logger;
        return options.IsHopEnabled(hop);
    }

    private static void Emit(SessionEventLogEntry entry)
    {
        Console.WriteLine(JsonSerializer.Serialize(entry, SerializerOptions));
    }

    private static string FormatMessage(
        string hop,
        string? a2aKind = null,
        string? aguiType = null,
        string? customName = null,
        long? seq = null,
        string? messageId = null)
    {
        var parts = new List<string> { hop };
        if (seq.HasValue)
        {
            parts.Add($"seq={seq.Value}");
        }
        if (!string.IsNullOrEmpty(a2aKind))
        {
            parts.Add($"a2aKind={a2aKind}");
        }
        if (!string.IsNullOrEmpty(aguiType))
        {
            parts.Add($"aguiType={aguiType}");
        }
        if (!string.IsNullOrEmpty(customName))
        {
            parts.Add($"customName={customName}");
        }
        if (!string.IsNullOrEmpty(messageId))
        {
            parts.Add($"msg={ShortId(messageId!)}");
        }
        return string.Join(' ', parts);
    }

    private static string ShortId(string id)
    {
        return id.Length <= 8 ? id : id[..8];
    }

    private static void ExtractA2ACorrelation(
        ParsedA2AEvent? parsed,
        out string? taskId,
        out string? messageId,
        out string? artifactId,
        out string? statusTimestamp)
    {
        taskId = null;
        messageId = null;
        artifactId = null;
        statusTimestamp = null;

        switch (parsed)
        {
            case ParsedAgentTask pt:
                taskId = pt.Task.Id;
                break;
            case ParsedAgentMessage pm:
                messageId = pm.Message.MessageId;
                taskId = pm.Message.TaskId;
                break;
            case ParsedTaskStatusUpdateEvent ps:
                taskId = ps.StatusUpdate.TaskId;
                statusTimestamp = FormatStatusTimestamp(ps.StatusUpdate.Status.Timestamp);
                break;
            case ParsedTaskArtifactUpdateEvent pa:
                taskId = pa.ArtifactUpdate.TaskId;
                artifactId = pa.ArtifactUpdate.Artifact?.ArtifactId;
                break;
        }
    }

    private static string? FormatStatusTimestamp(object? value)
    {
        return value switch
        {
            null => null,
            DateTimeOffset dto => dto.ToString("O"),
            DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt).ToString("O"),
            string s => s,
            _ => value.ToString(),
        };
    }

    private static string? ExtractPreview(ParsedA2AEvent? parsed, string? rawJson)
    {
        if (parsed is ParsedAgentMessage pm && pm.Message.Parts is not null)
        {
            foreach (var part in pm.Message.Parts)
            {
                if (part is TextPart tp && !string.IsNullOrEmpty(tp.Text))
                {
                    return tp.Text;
                }
            }
        }

        return rawJson;
    }

    private static string? ExtractAGUIPreview(AGUIBaseEvent agui)
    {
        return agui switch
        {
            TextMessageContentEvent c => c.Delta,
            ToolCallArgsEvent a => a.Delta,
            ToolCallResultEvent r => r.Content,
            RunErrorEvent e => e.Message,
            _ => null,
        };
    }
}
