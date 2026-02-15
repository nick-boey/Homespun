using Microsoft.Extensions.Logging;

namespace Homespun.Tests.Helpers;

/// <summary>
/// Logger provider that writes log output to files in the TestResults folder.
/// Used to redirect logging from console to file during test runs for better performance.
/// </summary>
public class TestFileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public TestFileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new TestFileLogger(categoryName, _logFilePath, _lock);

    public void Dispose()
    {
        // No resources to dispose
    }
}

/// <summary>
/// File-based logger that writes log messages to a specified file.
/// </summary>
public class TestFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private readonly object _lock;

    public TestFileLogger(string categoryName, string logFilePath, object lockObj)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath;
        _lock = lockObj;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {formatter(state, exception)}";
        if (exception != null)
        {
            message += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            File.AppendAllText(_logFilePath, message + Environment.NewLine);
        }
    }
}
