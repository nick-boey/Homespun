using System.Text.Json;
using Homespun.Features.Observability;
using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class ClientTelemetryControllerTests
{
    private ClientTelemetryController _controller = null!;
    private StringWriter _consoleOutput = null!;
    private TextWriter _originalOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _controller = new ClientTelemetryController();
        _originalOutput = Console.Out;
        _consoleOutput = new StringWriter();
        Console.SetOut(_consoleOutput);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(_originalOutput);
        _consoleOutput.Dispose();
    }

    [Test]
    public void ReceiveTelemetry_WithValidBatch_ReturnsAccepted()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "HomePage" }
            ]
        };

        // Act
        var result = _controller.ReceiveTelemetry(batch);

        // Assert
        Assert.That(result, Is.InstanceOf<AcceptedResult>());
    }

    [Test]
    public void ReceiveTelemetry_WithEmptyEvents_ReturnsBadRequest()
    {
        // Arrange
        var batch = new ClientTelemetryBatch { Events = [] };

        // Act
        var result = _controller.ReceiveTelemetry(batch);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ReceiveTelemetry_WithNullBatch_ReturnsBadRequest()
    {
        // Act
        var result = _controller.ReceiveTelemetry(null!);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ReceiveTelemetry_OutputsValidJson()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        Assert.DoesNotThrow(() => JsonDocument.Parse(output), "Output should be valid JSON");
    }

    [Test]
    public void ReceiveTelemetry_OutputsRequiredJsonFields()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        Assert.That(root.TryGetProperty("Timestamp", out _), Is.True);
        Assert.That(root.TryGetProperty("Level", out _), Is.True);
        Assert.That(root.TryGetProperty("Message", out _), Is.True);
        Assert.That(root.TryGetProperty("SourceContext", out _), Is.True);
    }

    [Test]
    public void ReceiveTelemetry_ExceptionEventsHaveErrorLevel()
    {
        // Arrange
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

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        Assert.That(json.RootElement.GetProperty("Level").GetString(), Is.EqualTo("Error"));
    }

    [Test]
    public void ReceiveTelemetry_PageViewEventsHaveInformationLevel()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        Assert.That(json.RootElement.GetProperty("Level").GetString(), Is.EqualTo("Information"));
    }

    [Test]
    public void ReceiveTelemetry_IncludesTelemetryFields()
    {
        // Arrange
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

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        Assert.That(root.GetProperty("TelemetryType").GetString(), Is.EqualTo("Event"));
        Assert.That(root.GetProperty("TelemetryName").GetString(), Is.EqualTo("ButtonClicked"));
        Assert.That(root.GetProperty("SessionId").GetString(), Is.EqualTo("test-session"));
    }

    [Test]
    public void ReceiveTelemetry_IncludesDependencyFields()
    {
        // Arrange
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
                    StatusCode = 200,
                    Properties = new Dictionary<string, string> { ["type"] = "HTTP", ["target"] = "localhost" }
                }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        var root = json.RootElement;

        Assert.That(root.GetProperty("DurationMs").GetDouble(), Is.EqualTo(150.5));
        Assert.That(root.GetProperty("Success").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("StatusCode").GetInt32(), Is.EqualTo(200));
    }

    [Test]
    public void ReceiveTelemetry_WithMultipleEvents_OutputsMultipleLines()
    {
        // Arrange
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

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var lines = _consoleOutput.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(3));

        // Verify each line is valid JSON
        foreach (var line in lines)
        {
            Assert.DoesNotThrow(() => JsonDocument.Parse(line));
        }
    }

    [Test]
    public void ReceiveTelemetry_WithNullSessionId_UsesUnknown()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = null,
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        Assert.That(json.RootElement.GetProperty("SessionId").GetString(), Is.EqualTo("unknown"));
    }

    [Test]
    public void ReceiveTelemetry_SourceContextIsClientTelemetry()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        Assert.That(json.RootElement.GetProperty("SourceContext").GetString(), Is.EqualTo("ClientTelemetry"));
    }

    [Test]
    public void ReceiveTelemetry_MessageContainsTelemetryInfo()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "HomePage" }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        var output = _consoleOutput.ToString().Trim();
        var json = JsonDocument.Parse(output);
        var message = json.RootElement.GetProperty("Message").GetString();
        Assert.That(message, Does.Contain("ClientTelemetry"));
        Assert.That(message, Does.Contain("PageView"));
        Assert.That(message, Does.Contain("HomePage"));
    }
}
