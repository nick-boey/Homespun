using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// A custom console formatter that outputs log entries as JSON.
/// Format: {"Timestamp":"...","Level":"...","Message":"...","SourceContext":"...","Exception":"..."}
/// </summary>
public sealed class JsonConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "json";

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

        var logObject = new
        {
            Timestamp = timestamp.ToString("O"), // ISO 8601 format
            Level = GetLogLevelString(logEntry.LogLevel),
            Message = message,
            SourceContext = GetShortCategoryName(logEntry.Category),
            Component = "Server",
            Exception = logEntry.Exception?.ToString()?.Replace(Environment.NewLine, " ")
        };

        textWriter.WriteLine(JsonSerializer.Serialize(logObject));
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
