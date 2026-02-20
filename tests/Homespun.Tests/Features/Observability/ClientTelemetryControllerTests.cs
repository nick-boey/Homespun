using Homespun.Features.Observability;
using Homespun.Shared.Models.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class ClientTelemetryControllerTests
{
    private ClientTelemetryController _controller = null!;
    private Mock<ILogger<ClientTelemetryController>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<ClientTelemetryController>>();
        _controller = new ClientTelemetryController(_loggerMock.Object);
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
    public void ReceiveTelemetry_LogsExceptionEventsAsError()
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
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[ClientTelemetry]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_LogsPageViewAsInformation()
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
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[ClientTelemetry]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_LogsEventAsInformation()
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
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[ClientTelemetry]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_LogsDependencyAsInformation()
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
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[ClientTelemetry]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_WithMultipleEvents_LogsEachEvent()
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

        // Assert - Should log 2 info (page views) and 1 error (exception)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_WithNullSessionId_LogsAsUnknown()
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
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SessionId=unknown")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void ReceiveTelemetry_WithNullProperties_LogsEmptyJson()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session",
            Events =
            [
                new ClientTelemetryEvent { Type = TelemetryEventType.PageView, Name = "TestPage", Properties = null }
            ]
        };

        // Act
        _controller.ReceiveTelemetry(batch);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Properties={}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
