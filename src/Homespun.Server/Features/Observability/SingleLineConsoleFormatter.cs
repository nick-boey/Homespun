using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// A custom console formatter that outputs log entries in a single-line format.
/// Format: [timestamp] [level] [category] message
/// </summary>
public sealed class SingleLineConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "singleline";

    private readonly SingleLineConsoleFormatterOptions _options;

    public SingleLineConsoleFormatter(IOptionsMonitor<SingleLineConsoleFormatterOptions> options)
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

        // Build the single-line log entry
        var timestamp = _options.UseUtcTimestamp
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Now;

        var logLevel = GetLogLevelString(logEntry.LogLevel);
        var category = GetShortCategoryName(logEntry.Category);

        // Format: [timestamp] [level] [category] message
        textWriter.Write('[');
        textWriter.Write(timestamp.ToString(_options.TimestampFormat));
        textWriter.Write("] [");
        WriteLogLevel(textWriter, logEntry.LogLevel, logLevel);
        textWriter.Write("] [");
        textWriter.Write(category);
        textWriter.Write("] ");
        textWriter.Write(message);

        // Include exception details on the same line if present
        if (logEntry.Exception is not null)
        {
            textWriter.Write(" | Exception: ");
            textWriter.Write(logEntry.Exception.GetType().Name);
            textWriter.Write(": ");
            textWriter.Write(logEntry.Exception.Message.Replace(Environment.NewLine, " "));
        }

        textWriter.WriteLine();
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private void WriteLogLevel(TextWriter textWriter, LogLevel logLevel, string logLevelString)
    {
        if (!_options.ColorEnabled)
        {
            textWriter.Write(logLevelString);
            return;
        }

        var (foreground, background) = GetLogLevelColors(logLevel);

        if (foreground.HasValue)
        {
            textWriter.Write(GetAnsiColorCode(foreground.Value, background));
        }

        textWriter.Write(logLevelString);

        if (foreground.HasValue)
        {
            textWriter.Write("\x1b[0m"); // Reset
        }
    }

    private static (ConsoleColor? Foreground, ConsoleColor? Background) GetLogLevelColors(LogLevel logLevel) =>
        logLevel switch
        {
            LogLevel.Trace => (ConsoleColor.Gray, null),
            LogLevel.Debug => (ConsoleColor.Gray, null),
            LogLevel.Information => (ConsoleColor.DarkGreen, null),
            LogLevel.Warning => (ConsoleColor.Yellow, null),
            LogLevel.Error => (ConsoleColor.Black, ConsoleColor.DarkRed),
            LogLevel.Critical => (ConsoleColor.White, ConsoleColor.DarkRed),
            _ => (null, null)
        };

    private static string GetAnsiColorCode(ConsoleColor foreground, ConsoleColor? background)
    {
        var foregroundCode = foreground switch
        {
            ConsoleColor.Black => 30,
            ConsoleColor.DarkRed => 31,
            ConsoleColor.DarkGreen => 32,
            ConsoleColor.DarkYellow => 33,
            ConsoleColor.DarkBlue => 34,
            ConsoleColor.DarkMagenta => 35,
            ConsoleColor.DarkCyan => 36,
            ConsoleColor.Gray => 37,
            ConsoleColor.DarkGray => 90,
            ConsoleColor.Red => 91,
            ConsoleColor.Green => 92,
            ConsoleColor.Yellow => 93,
            ConsoleColor.Blue => 94,
            ConsoleColor.Magenta => 95,
            ConsoleColor.Cyan => 96,
            ConsoleColor.White => 97,
            _ => 37
        };

        if (background.HasValue)
        {
            var backgroundCode = background.Value switch
            {
                ConsoleColor.Black => 40,
                ConsoleColor.DarkRed => 41,
                ConsoleColor.DarkGreen => 42,
                ConsoleColor.DarkYellow => 43,
                ConsoleColor.DarkBlue => 44,
                ConsoleColor.DarkMagenta => 45,
                ConsoleColor.DarkCyan => 46,
                ConsoleColor.Gray => 47,
                ConsoleColor.DarkGray => 100,
                ConsoleColor.Red => 101,
                ConsoleColor.Green => 102,
                ConsoleColor.Yellow => 103,
                ConsoleColor.Blue => 104,
                ConsoleColor.Magenta => 105,
                ConsoleColor.Cyan => 106,
                ConsoleColor.White => 107,
                _ => 40
            };
            return $"\x1b[{foregroundCode};{backgroundCode}m";
        }

        return $"\x1b[{foregroundCode}m";
    }

    private static string GetShortCategoryName(string category)
    {
        // Extract just the class name from the full namespace
        // e.g., "Homespun.Features.Commands.CommandRunner" -> "CommandRunner"
        var lastDotIndex = category.LastIndexOf('.');
        return lastDotIndex >= 0 ? category[(lastDotIndex + 1)..] : category;
    }
}

/// <summary>
/// Options for the single-line console formatter.
/// </summary>
public sealed class SingleLineConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// Gets or sets whether to enable ANSI color output.
    /// </summary>
    public bool ColorEnabled { get; set; } = true;
}
