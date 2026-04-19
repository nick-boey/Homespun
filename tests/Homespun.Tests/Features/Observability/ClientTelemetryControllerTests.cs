using Homespun.Features.Observability;
using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class ClientTelemetryControllerTests
{
    private ClientTelemetryController _controller = null!;
    private CapturingLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new CapturingLogger();
        _controller = new ClientTelemetryController(_logger);
    }

    [Test]
    public void ReceiveTelemetry_WithValidBatch_ReturnsAccepted()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "HomePage" }
            ]
        };

        var result = _controller.ReceiveTelemetry(batch);

        Assert.That(result, Is.InstanceOf<AcceptedResult>());
    }

    [Test]
    public void ReceiveTelemetry_WithEmptyEvents_ReturnsBadRequest()
    {
        var batch = new ClientTelemetryBatch { Events = [] };

        var result = _controller.ReceiveTelemetry(batch);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ReceiveTelemetry_WithNullBatch_ReturnsBadRequest()
    {
        var result = _controller.ReceiveTelemetry(null!);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ReceiveTelemetry_ExceptionEvents_LogAtErrorLevel()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Exception,
                    Name = "InvalidOperationException",
                    Properties = new Dictionary<string, string> { ["message"] = "Test error" }
                }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        Assert.That(_logger.Entries, Has.Count.EqualTo(1));
        Assert.That(_logger.Entries[0].Level, Is.EqualTo(LogLevel.Error));
    }

    [Test]
    public void ReceiveTelemetry_NonExceptionEvents_LogAtInformationLevel()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        Assert.That(_logger.Entries, Has.Count.EqualTo(1));
        Assert.That(_logger.Entries[0].Level, Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public void ReceiveTelemetry_EmitsScopedTelemetryFields()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Event,
                    Name = "ButtonClicked",
                    Properties = new Dictionary<string, string> { ["buttonId"] = "submit" }
                }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        var entry = _logger.Entries.Single();
        Assert.That(entry.Scope, Is.Not.Null);
        Assert.That(entry.Scope!["SourceContext"], Is.EqualTo("ClientTelemetry"));
        Assert.That(entry.Scope!["Component"], Is.EqualTo("Client"));
        Assert.That(entry.Scope!["TelemetryType"], Is.EqualTo("Event"));
        Assert.That(entry.Scope!["TelemetryName"], Is.EqualTo("ButtonClicked"));
        Assert.That(entry.Scope!["SessionId"], Is.EqualTo("test-session"));
        Assert.That(entry.Scope!.TryGetValue("Properties", out var props), Is.True);
        Assert.That(props, Is.EqualTo(new Dictionary<string, string> { ["buttonId"] = "submit" }));
    }

    [Test]
    public void ReceiveTelemetry_EmitsDependencyFields_WhenProvided()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Dependency,
                    Name = "GET /api/projects",
                    DurationMs = 150.5,
                    Success = true,
                    StatusCode = 200
                }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        var scope = _logger.Entries.Single().Scope!;
        Assert.That(scope["DurationMs"], Is.EqualTo(150.5));
        Assert.That(scope["Success"], Is.EqualTo(true));
        Assert.That(scope["StatusCode"], Is.EqualTo(200));
    }

    [Test]
    public void ReceiveTelemetry_OmitsDependencyFields_WhenAbsent()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        var scope = _logger.Entries.Single().Scope!;
        Assert.That(scope.ContainsKey("DurationMs"), Is.False);
        Assert.That(scope.ContainsKey("Success"), Is.False);
        Assert.That(scope.ContainsKey("StatusCode"), Is.False);
    }

    [Test]
    public void ReceiveTelemetry_WithNullSessionId_ScopesSessionIdAsUnknown()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = null,
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        var scope = _logger.Entries.Single().Scope!;
        Assert.That(scope["SessionId"], Is.EqualTo("unknown"));
    }

    [Test]
    public void ReceiveTelemetry_MessageContainsTelemetryInfo()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "HomePage" }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        var message = _logger.Entries.Single().Message;
        Assert.That(message, Does.Contain("ClientTelemetry"));
        Assert.That(message, Does.Contain("PageView"));
        Assert.That(message, Does.Contain("HomePage"));
    }

    [Test]
    public void ReceiveTelemetry_WithMultipleEvents_LogsOnceEach()
    {
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "Page1" },
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "Page2" },
                new ClientTelemetryEvent { Type = TelemetryEventType.Exception, Name = "Error1" }
            ]
        };

        _controller.ReceiveTelemetry(batch);

        Assert.That(_logger.Entries, Has.Count.EqualTo(3));
        Assert.That(_logger.Entries[2].Level, Is.EqualTo(LogLevel.Error));
    }

    /// <summary>
    /// Minimal ILogger that records every entry with a flattened snapshot of the
    /// active logging scope so tests can verify both the log level and the
    /// structured attributes ClientTelemetryController pushes via BeginScope.
    /// </summary>
    private sealed class CapturingLogger : ILogger<ClientTelemetryController>
    {
        private readonly Stack<IDictionary<string, object?>> _scopeStack = new();

        public List<LogRecord> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                var snapshot = kvps.ToDictionary(k => k.Key, k => k.Value);
                _scopeStack.Push(snapshot);
                return new PopOnDispose(_scopeStack);
            }
            return new PopOnDispose(null);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scope = _scopeStack.Count > 0
                ? new Dictionary<string, object?>(_scopeStack.Peek())
                : null;

            Entries.Add(new LogRecord(
                logLevel,
                formatter(state, exception),
                scope));
        }

        private sealed class PopOnDispose(Stack<IDictionary<string, object?>>? stack) : IDisposable
        {
            public void Dispose()
            {
                if (stack is { Count: > 0 })
                {
                    stack.Pop();
                }
            }
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message, IDictionary<string, object?>? Scope);
}
