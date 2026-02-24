using System.Text.Json;
using Homespun.Features.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class JsonConsoleFormatterTests
{
    private JsonConsoleFormatter _formatter = null!;
    private StringWriter _output = null!;
    private PromtailJsonFormatterOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _output = new StringWriter();
        _options = new PromtailJsonFormatterOptions
        {
            UseUtcTimestamp = true
        };
        var optionsMonitor = new TestOptionsMonitor<PromtailJsonFormatterOptions>(_options);
        _formatter = new JsonConsoleFormatter(optionsMonitor);
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
    }

    [Test]
    public void Write_OutputsValidJson()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "Homespun.Features.Commands.CommandRunner",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var result = _output.ToString().Trim();
        Assert.DoesNotThrow(() => JsonDocument.Parse(result), "Output should be valid JSON");
    }

    [Test]
    public void Write_OutputsSingleLine()
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
        Assert.That(result.Count(c => c == '\n'), Is.EqualTo(1), "Should output exactly one line");
    }

    [Test]
    public void Write_IncludesTimestampInIso8601Format()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var timestamp = json.RootElement.GetProperty("Timestamp").GetString();
        Assert.That(timestamp, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"));
    }

    [TestCase(LogLevel.Trace, "Trace")]
    [TestCase(LogLevel.Debug, "Debug")]
    [TestCase(LogLevel.Information, "Information")]
    [TestCase(LogLevel.Warning, "Warning")]
    [TestCase(LogLevel.Error, "Error")]
    [TestCase(LogLevel.Critical, "Critical")]
    public void Write_FormatsLogLevelCorrectly(LogLevel logLevel, string expectedLevel)
    {
        // Arrange
        var logEntry = CreateLogEntry(logLevel, "TestCategory", "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var level = json.RootElement.GetProperty("Level").GetString();
        Assert.That(level, Is.EqualTo(expectedLevel));
    }

    [Test]
    public void Write_IncludesMessage()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message with special characters: \"quotes\" and \\backslash");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var message = json.RootElement.GetProperty("Message").GetString();
        Assert.That(message, Is.EqualTo("Test message with special characters: \"quotes\" and \\backslash"));
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
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var sourceContext = json.RootElement.GetProperty("SourceContext").GetString();
        Assert.That(sourceContext, Is.EqualTo("GitHubService"));
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
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var sourceContext = json.RootElement.GetProperty("SourceContext").GetString();
        Assert.That(sourceContext, Is.EqualTo("SimpleCategory"));
    }

    [Test]
    public void Write_IncludesExceptionWhenPresent()
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
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var exceptionText = json.RootElement.GetProperty("Exception").GetString();
        Assert.That(exceptionText, Does.Contain("InvalidOperationException"));
        Assert.That(exceptionText, Does.Contain("Something went wrong"));
    }

    [Test]
    public void Write_OmitsExceptionWhenNotPresent()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("Exception", out _), Is.False, "Exception should be omitted when not present");
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

        var json = JsonDocument.Parse(result.Trim());
        var exceptionText = json.RootElement.GetProperty("Exception").GetString();
        Assert.That(exceptionText, Does.Not.Contain("\n"));
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
    public void FormatterName_IsJson()
    {
        Assert.That(JsonConsoleFormatter.FormatterName, Is.EqualTo("json"));
    }

    [Test]
    public void Write_IncludesIssueIdFromScope_WhenPresent()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        var scopeProvider = new TestScopeProvider();
        scopeProvider.Push(new Dictionary<string, object?>
        {
            [IssueLogScope.IssueIdKey] = "ABC123"
        });

        // Act
        _formatter.Write(logEntry, scopeProvider, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("IssueId", out var issueIdElement), Is.True, "Should have IssueId field");
        Assert.That(issueIdElement.GetString(), Is.EqualTo("ABC123"));
    }

    [Test]
    public void Write_IncludesProjectNameFromScope_WhenPresent()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        var scopeProvider = new TestScopeProvider();
        scopeProvider.Push(new Dictionary<string, object?>
        {
            [IssueLogScope.ProjectNameKey] = "TestProject"
        });

        // Act
        _formatter.Write(logEntry, scopeProvider, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("ProjectName", out var projectNameElement), Is.True, "Should have ProjectName field");
        Assert.That(projectNameElement.GetString(), Is.EqualTo("TestProject"));
    }

    [Test]
    public void Write_IncludesBothIssueIdAndProjectName_WhenBothPresent()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        var scopeProvider = new TestScopeProvider();
        scopeProvider.Push(new Dictionary<string, object?>
        {
            [IssueLogScope.IssueIdKey] = "DEF456",
            [IssueLogScope.ProjectNameKey] = "AnotherProject"
        });

        // Act
        _formatter.Write(logEntry, scopeProvider, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("IssueId", out var issueIdElement), Is.True);
        Assert.That(issueIdElement.GetString(), Is.EqualTo("DEF456"));
        Assert.That(json.RootElement.TryGetProperty("ProjectName", out var projectNameElement), Is.True);
        Assert.That(projectNameElement.GetString(), Is.EqualTo("AnotherProject"));
    }

    [Test]
    public void Write_OmitsIssueId_WhenNotInScope()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        // Act - no scope provider
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("IssueId", out _), Is.False, "Should not have IssueId field when not in scope");
    }

    [Test]
    public void Write_OmitsIssueId_WhenScopeHasEmptyIssueId()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        var scopeProvider = new TestScopeProvider();
        scopeProvider.Push(new Dictionary<string, object?>
        {
            [IssueLogScope.IssueIdKey] = ""
        });

        // Act
        _formatter.Write(logEntry, scopeProvider, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.TryGetProperty("IssueId", out _), Is.False, "Should not have IssueId field when empty");
    }

    [Test]
    public void Write_ContainsAllRequiredFields()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Warning,
            "Homespun.Features.Git.GitCloneService",
            "Clone failed");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        var root = json.RootElement;

        Assert.That(root.TryGetProperty("Timestamp", out _), Is.True, "Should have Timestamp field");
        Assert.That(root.TryGetProperty("Level", out _), Is.True, "Should have Level field");
        Assert.That(root.TryGetProperty("Message", out _), Is.True, "Should have Message field");
        Assert.That(root.TryGetProperty("SourceContext", out _), Is.True, "Should have SourceContext field");
        Assert.That(root.TryGetProperty("Component", out _), Is.True, "Should have Component field");
        // Note: Exception, IssueId, and ProjectName are optional fields that are only included when present
    }

    [Test]
    public void Write_IncludesComponentFieldSetToServer()
    {
        // Arrange
        var logEntry = CreateLogEntry(
            LogLevel.Information,
            "TestCategory",
            "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);

        // Assert
        var json = JsonDocument.Parse(_output.ToString().Trim());
        Assert.That(json.RootElement.GetProperty("Component").GetString(), Is.EqualTo("Server"));
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

    private class TestScopeProvider : IExternalScopeProvider
    {
        private readonly Stack<object> _scopes = new();

        public void Push(object state)
        {
            _scopes.Push(state);
        }

        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
            foreach (var scope in _scopes)
            {
                callback(scope, state);
            }
        }

        IDisposable IExternalScopeProvider.Push(object? state)
        {
            if (state is not null)
            {
                _scopes.Push(state);
            }
            return new NoopDisposable();
        }

        private class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
