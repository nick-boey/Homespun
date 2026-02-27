using Homespun.Features.ClaudeCode.Controllers;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Containers.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Sessions;
using Homespun.Shared.Requests;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode.Controllers;

[TestFixture]
public class SessionsControllerTests
{
    private SessionsController _controller = null!;
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IProjectService> _projectServiceMock = null!;
    private Mock<IContainerQueryService> _containerServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _projectServiceMock = new Mock<IProjectService>();
        _containerServiceMock = new Mock<IContainerQueryService>();
        _controller = new SessionsController(
            _sessionServiceMock.Object,
            _projectServiceMock.Object,
            _containerServiceMock.Object);
    }

    [Test]
    public async Task SendMessage_WithNullMode_UsesSessionMode()
    {
        // Arrange - Create a session in Plan mode
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = "entity-456",
            ProjectId = "project-789",
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        _sessionServiceMock.Setup(s => s.GetSession("session-123")).Returns(session);
        _sessionServiceMock.Setup(s => s.SendMessageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<SessionMode>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Create request with no mode specified (will be null)
        var request = new SendMessageRequest { Message = "Hello" };

        // Act
        var result = await _controller.SendMessage("session-123", request);

        // Assert - should use the session's Plan mode
        Assert.That(result, Is.InstanceOf<AcceptedResult>());
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-123",
            "Hello",
            SessionMode.Plan, // Session's mode, not Build
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SendMessage_WithExplicitMode_UsesRequestMode()
    {
        // Arrange - Create a session in Plan mode but send message with Build mode
        var session = new ClaudeSession
        {
            Id = "session-123",
            EntityId = "entity-456",
            ProjectId = "project-789",
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Plan,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        _sessionServiceMock.Setup(s => s.GetSession("session-123")).Returns(session);
        _sessionServiceMock.Setup(s => s.SendMessageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<SessionMode>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Create request with explicit Build mode
        var request = new SendMessageRequest { Message = "Hello", Mode = SessionMode.Build };

        // Act
        var result = await _controller.SendMessage("session-123", request);

        // Assert - should use the explicitly specified Build mode
        Assert.That(result, Is.InstanceOf<AcceptedResult>());
        _sessionServiceMock.Verify(s => s.SendMessageAsync(
            "session-123",
            "Hello",
            SessionMode.Build, // Explicit mode from request
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SendMessage_SessionNotFound_ReturnsNotFound()
    {
        // Arrange
        _sessionServiceMock.Setup(s => s.GetSession("nonexistent")).Returns((ClaudeSession?)null);
        var request = new SendMessageRequest { Message = "Hello" };

        // Act
        var result = await _controller.SendMessage("nonexistent", request);

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }
}
