using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for ClaudeCodeHub methods.
/// </summary>
[TestFixture]
public class ClaudeCodeHubTests
{
    private Mock<IClaudeSessionService> _sessionServiceMock = null!;
    private Mock<IMessageCacheStore> _messageCacheStoreMock = null!;
    private ClaudeCodeHub _hub = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionServiceMock = new Mock<IClaudeSessionService>();
        _messageCacheStoreMock = new Mock<IMessageCacheStore>();
        _hub = new ClaudeCodeHub(_sessionServiceMock.Object, _messageCacheStoreMock.Object);
    }

    #region GetCachedMessageCount Tests

    [Test]
    public async Task GetCachedMessageCount_SessionExists_ReturnsMessageCount()
    {
        // Arrange
        var sessionId = "test-session";
        var summary = new SessionCacheSummary(
            SessionId: sessionId,
            EntityId: "entity-1",
            ProjectId: "project-1",
            MessageCount: 42,
            CreatedAt: DateTime.UtcNow,
            LastMessageAt: DateTime.UtcNow,
            Mode: SessionMode.Build,
            Model: "sonnet"
        );
        _messageCacheStoreMock
            .Setup(x => x.GetSessionSummaryAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        // Act
        var result = await _hub.GetCachedMessageCount(sessionId);

        // Assert
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task GetCachedMessageCount_SessionDoesNotExist_ReturnsZero()
    {
        // Arrange
        var sessionId = "nonexistent-session";
        _messageCacheStoreMock
            .Setup(x => x.GetSessionSummaryAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionCacheSummary?)null);

        // Act
        var result = await _hub.GetCachedMessageCount(sessionId);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public async Task GetCachedMessageCount_EmptySession_ReturnsZero()
    {
        // Arrange
        var sessionId = "empty-session";
        var summary = new SessionCacheSummary(
            SessionId: sessionId,
            EntityId: "entity-1",
            ProjectId: "project-1",
            MessageCount: 0,
            CreatedAt: DateTime.UtcNow,
            LastMessageAt: DateTime.UtcNow,
            Mode: SessionMode.Build,
            Model: "sonnet"
        );
        _messageCacheStoreMock
            .Setup(x => x.GetSessionSummaryAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        // Act
        var result = await _hub.GetCachedMessageCount(sessionId);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    #endregion
}
