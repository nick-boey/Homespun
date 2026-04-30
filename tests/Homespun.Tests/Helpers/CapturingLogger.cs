using Microsoft.Extensions.Logging;

namespace Homespun.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ILogger{T}"/> that snapshots each entry's formatted
/// message, structured tags, and active scopes synchronously inside
/// <see cref="Log{TState}"/>.
///
/// <para>
/// This is the only safe way to assert on <c>[LoggerMessage]</c> output when
/// the source generator is <c>Microsoft.Gen.Logging</c> (the high-performance
/// generator brought in transitively by
/// <c>Microsoft.Extensions.Telemetry.Abstractions</c>). That generator stores
/// each call's tag array on a pooled <c>LoggerMessageHelper.ThreadLocalState</c>
/// and clears it as soon as <c>logger.Log(...)</c> returns, so a stock
/// <c>Mock&lt;ILogger&lt;T&gt;&gt;</c> verification that runs
/// <c>state.ToString()</c> at verify-time sees an empty state.
/// </para>
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogEntry> Entries { get; } = new();
    public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = new();
    public bool Enabled { get; set; } = true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object>> dict)
        {
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in dict)
            {
                snapshot[kv.Key] = kv.Value;
            }
            Scopes.Add(snapshot);
        }
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => Enabled;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var formatted = formatter(state, exception);
        var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
        {
            foreach (var kv in kvs)
            {
                if (kv.Key == "{OriginalFormat}") continue;
                tags[kv.Key] = kv.Value;
            }
        }
        Entries.Add(new CapturedLogEntry(logLevel, eventId, formatted, tags, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

public sealed record CapturedLogEntry(
    LogLevel Level,
    EventId EventId,
    string FormattedMessage,
    IReadOnlyDictionary<string, object?> Tags,
    Exception? Exception);
