using Homespun.Features.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class SingleLineConsoleFormatterTests
{
    private SingleLineConsoleFormatter _formatter = null!;
    private StringWriter _output = null!;
    private SingleLineConsoleFormatterOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _output = new StringWriter();
        _options = new SingleLineConsoleFormatterOptions
        {
            TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff",
            UseUtcTimestamp = true,
            ColorEnabled = false
        };
        var optionsMonitor = new TestOptionsMonitor<SingleLineConsoleFormatterOptions>(_options);
        _formatter = new SingleLineConsoleFormatter(optionsMonitor);
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
    }

    [Test]
    public void Write_FormatsLogEntryOnSingleLine()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "Homespun.Features.Commands.CommandRunner",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result.Count(c => c == '\n'), Is.EqualTo(1), "Should output exactly one line");
        Assert.That(result, Does.Contain("[INF]"));
        Assert.That(result, Does.Contain("[CommandRunner]"));
        Assert.That(result, Does.Contain("Test message"));
    }

    [Test]
    public void Write_IncludesTimestamp()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        // Timestamp should be in format [yyyy-MM-dd HH:mm:ss.fff]
        Assert.That(result, Does.Match(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]"));
    }

    [TestCase(LogLevel.Trace, "TRC")]
    [TestCase(LogLevel.Debug, "DBG")]
    [TestCase(LogLevel.Information, "INF")]
    [TestCase(LogLevel.Warning, "WRN")]
    [TestCase(LogLevel.Error, "ERR")]
    [TestCase(LogLevel.Critical, "CRT")]
    public void Write_FormatsLogLevelCorrectly(LogLevel logLevel, string expectedAbbreviation)
    {
        // Arrange
        var logEntry = CreateLogEntry(logLevel, "TestCategory", "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result, Does.Contain($"[{expectedAbbreviation}]"));
    }

    [Test]
    public void Write_ExtractsClassNameFromFullNamespace()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "Homespun.Features.GitHub.GitHubService",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result, Does.Contain("[GitHubService]"));
        Assert.That(result, Does.Not.Contain("Homespun.Features.GitHub.GitHubService"));
    }

    [Test]
    public void Write_HandlesSimpleCategoryName()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "SimpleCategory",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result, Does.Contain("[SimpleCategory]"));
    }

    [Test]
    public void Write_IncludesExceptionOnSameLine()
    {
        // Arrange
        var exception = new InvalidOperationException("Something went wrong");
        var logEntry = CreateLogEntry(
            LogLevel.Error,
            "TestCategory",
            "Error occurred",
            exception);

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result.Count(c => c == '\n'), Is.EqualTo(1), "Should still be single line with exception");
        Assert.That(result, Does.Contain("| Exception: InvalidOperationException: Something went wrong"));
    }

    [Test]
    public void Write_HandlesMultilineExceptionMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Line1\nLine2\nLine3");
        var logEntry = CreateLogEntry(
            LogLevel.Error,
            "TestCategory",
            "Error occurred",
            exception);

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result.Count(c => c == '\n'), Is.EqualTo(1), "Exception message newlines should be replaced");
        Assert.That(result, Does.Not.Contain("\nLine2"));
    }

    [Test]
    public void Write_ReturnsEarly_WhenMessageIsNull()
    {
        // Arrange
        var logEntry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            null!,
            null,
            (state, ex) => null!);

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Write_OutputFormat_MatchesExpectedPattern()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Warning,
            "Homespun.Features.Git.GitCloneService",
            "Clone failed");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString();
        // Format should be: [timestamp] [level] [category] message
        Assert.That(result, Does.Match(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[WRN\] \[GitCloneService\] Clone failed\n$"));
    }

    [Test]
    public void FormatterName_IsSingleline()
    {
        Assert.That(SingleLineConsoleFormatter.FormatterName, Is.EqualTo("singleline"));
    }

    private static LogEntry<string> CreateLogEntry(
        LogLevel logLevel,
        string category,
        string message,
        Exception? exception = null)
    {
        return new LogEntry<string>(
            logLevel,
            category,
            new EventId(0),
            message,
            exception,
            (state, ex) => state);
    }

    private class TestOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
