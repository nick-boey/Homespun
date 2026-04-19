using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// A custom console formatter that outputs log entries as JSON.
/// Baseline fields: Timestamp, Level, Message, SourceContext, Component, Exception.
/// Any additional key/value pair pushed via an ILogger scope is merged into the
/// JSON output as a top-level field so downstream consumers (Promtail pipeline
/// stages, Loki label extraction) see the same shape that OTLP attributes carry.
/// See <see cref="IssueLogScope"/> for the canonical issue-context keys.
/// </summary>
public sealed class JsonConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "json";

    /// <summary>
    /// Keys the formatter fully controls. Scope values matching these keys are
    /// dropped so callers can't clobber the canonical envelope fields.
    /// <c>SourceContext</c> and <c>Component</c> are intentionally NOT in this
    /// set — client-telemetry logs, for example, advertise themselves as
    /// <c>SourceContext="ClientTelemetry"</c>, <c>Component="Client"</c> via scope.
    /// </summary>
    private static readonly HashSet<string> ProtectedReservedKeys = new(StringComparer.Ordinal)
    {
        "Timestamp",
        "Level",
        "Message",
        "Exception"
    };

    private readonly PromtailJsonFormatterOptions _options;

    public JsonConsoleFormatter(IOptionsMonitor<PromtailJsonFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
        {
            return;
        }

        var timestamp = _options.UseUtcTimestamp
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Now;

        var logObject = new Dictionary<string, object?>
        {
            ["Timestamp"] = timestamp.ToString("O"),
            ["Level"] = GetLogLevelString(logEntry.LogLevel),
            ["Message"] = message,
            ["SourceContext"] = GetShortCategoryName(logEntry.Category),
            ["Component"] = "Server"
        };

        MergeScopeValues(scopeProvider, logObject);

        if (logEntry.Exception is not null)
        {
            logObject["Exception"] = logEntry.Exception.ToString()?.Replace("\r\n", " ").Replace("\n", " ");
        }

        textWriter.WriteLine(JsonSerializer.Serialize(logObject));
    }

    private static void MergeScopeValues(IExternalScopeProvider? scopeProvider, Dictionary<string, object?> logObject)
    {
        if (scopeProvider is null)
        {
            return;
        }

        scopeProvider.ForEachScope((scope, state) =>
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object?>> scopeList)
            {
                foreach (var item in scopeList)
                {
                    MergeEntry(state, item.Key, item.Value);
                }
            }
            else if (scope is IEnumerable<KeyValuePair<string, object?>> scopeEnumerable)
            {
                foreach (var item in scopeEnumerable)
                {
                    MergeEntry(state, item.Key, item.Value);
                }
            }
        }, logObject);
    }

    private static void MergeEntry(Dictionary<string, object?> logObject, string key, object? value)
    {
        if (string.IsNullOrEmpty(key) || value is null)
        {
            return;
        }

        // Skip empty strings so callers can pass `scope[Foo] = maybeEmpty`
        // without polluting output with blank values — matches the behaviour
        // of the original IssueLogScope-specific extraction.
        if (value is string str && str.Length == 0)
        {
            return;
        }

        // Skip the implicit {OriginalFormat} key that ILogger.Log pushes
        // alongside message-template parameters — its value is the unrendered
        // template string, which would be noise in Loki.
        if (key == "{OriginalFormat}")
        {
            return;
        }

        if (ProtectedReservedKeys.Contains(key))
        {
            return;
        }

        logObject[key] = value;
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => "Unknown"
    };

    private static string GetShortCategoryName(string category)
    {
        // Extract just the class name from the full namespace
        // e.g., "Homespun.Features.Commands.CommandRunner" -> "CommandRunner"
        var lastDotIndex = category.LastIndexOf('.');
        return lastDotIndex >= 0 ? category[(lastDotIndex + 1)..] : category;
    }
}

/// <summary>
/// Options for the Promtail JSON console formatter.
/// </summary>
public sealed class PromtailJsonFormatterOptions : ConsoleFormatterOptions
{
}
