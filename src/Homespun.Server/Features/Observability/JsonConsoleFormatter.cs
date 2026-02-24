using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Observability;

/// <summary>
/// A custom console formatter that outputs log entries as JSON.
/// Format: {"Timestamp":"...","Level":"...","Message":"...","SourceContext":"...","Component":"...","IssueId":"...","ProjectName":"...","Exception":"..."}
/// IssueId and ProjectName are included when available from logging scope (see IssueLogScope).
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

        // Extract issue context from scope
        string? issueId = null;
        string? projectName = null;
        ExtractScopeValues(scopeProvider, ref issueId, ref projectName);

        // Build log object with optional fields
        var logObject = new Dictionary<string, object?>
        {
            ["Timestamp"] = timestamp.ToString("O"), // ISO 8601 format
            ["Level"] = GetLogLevelString(logEntry.LogLevel),
            ["Message"] = message,
            ["SourceContext"] = GetShortCategoryName(logEntry.Category),
            ["Component"] = "Server"
        };

        if (!string.IsNullOrEmpty(issueId))
        {
            logObject["IssueId"] = issueId;
        }

        if (!string.IsNullOrEmpty(projectName))
        {
            logObject["ProjectName"] = projectName;
        }

        if (logEntry.Exception is not null)
        {
            logObject["Exception"] = logEntry.Exception.ToString()?.Replace(Environment.NewLine, " ");
        }

        textWriter.WriteLine(JsonSerializer.Serialize(logObject));
    }

    /// <summary>
    /// Helper class to hold scope values that can be modified in the ForEachScope callback.
    /// </summary>
    private sealed class ScopeValueHolder
    {
        public string? IssueId { get; set; }
        public string? ProjectName { get; set; }
    }

    private static void ExtractScopeValues(IExternalScopeProvider? scopeProvider, ref string? issueId, ref string? projectName)
    {
        if (scopeProvider is null)
        {
            return;
        }

        var holder = new ScopeValueHolder();

        scopeProvider.ForEachScope((scope, state) =>
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object?>> scopeItems)
            {
                foreach (var item in scopeItems)
                {
                    if (item.Key == IssueLogScope.IssueIdKey && item.Value is string id)
                    {
                        state.IssueId = id;
                    }
                    else if (item.Key == IssueLogScope.ProjectNameKey && item.Value is string name)
                    {
                        state.ProjectName = name;
                    }
                }
            }
            else if (scope is IEnumerable<KeyValuePair<string, object?>> scopeDict)
            {
                foreach (var item in scopeDict)
                {
                    if (item.Key == IssueLogScope.IssueIdKey && item.Value is string id)
                    {
                        state.IssueId = id;
                    }
                    else if (item.Key == IssueLogScope.ProjectNameKey && item.Value is string name)
                    {
                        state.ProjectName = name;
                    }
                }
            }
        }, holder);

        issueId = holder.IssueId;
        projectName = holder.ProjectName;
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
