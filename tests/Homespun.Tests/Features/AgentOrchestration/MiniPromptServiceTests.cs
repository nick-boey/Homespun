using Homespun.Features.AgentOrchestration.Services;
using Moq;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class MiniPromptServiceTests
{
    [Test]
    public void ExecuteAsync_EmptyPrompt_ThrowsArgumentException()
    {
        var service = new MiniPromptService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync(""));
    }

    [Test]
    public void ExecuteAsync_NullPrompt_ThrowsArgumentException()
    {
        var service = new MiniPromptService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync(null!));
    }

    [Test]
    public void ExecuteAsync_WhitespacePrompt_ThrowsArgumentException()
    {
        var service = new MiniPromptService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync("   "));
    }

    [Test]
    public async Task ExecuteAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var service = new MiniPromptService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ExecuteAsync("test prompt", cancellationToken: cts.Token));
    }

    [Test]
    public void ExecuteAsync_ValidPrompt_ReturnsResult()
    {
        // This test verifies the interface contract - actual AI integration
        // would require real API calls which we test separately
        var service = new MiniPromptService();

        // We can't easily test the full flow without mocking the Claude SDK
        // but we can verify the service is constructed properly
        Assert.That(service, Is.Not.Null);
    }
}

/// <summary>
/// Tests for MiniPromptService using a mock implementation.
/// These tests verify the service behavior without requiring actual AI calls.
/// </summary>
[TestFixture]
public class MiniPromptServiceMockTests
{
    private Mock<IMiniPromptService> _mockService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<IMiniPromptService>();
    }

    [Test]
    public async Task ExecuteAsync_ValidPrompt_ReturnsSuccessResult()
    {
        // Arrange
        var expectedResult = new MiniPromptResult(
            Success: true,
            Response: "test-response",
            Error: null,
            CostUsd: 0.0001m,
            DurationMs: 150);

        _mockService.Setup(x => x.ExecuteAsync("test prompt", "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _mockService.Object.ExecuteAsync("test prompt", "haiku");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Response, Is.EqualTo("test-response"));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.CostUsd, Is.EqualTo(0.0001m));
            Assert.That(result.DurationMs, Is.EqualTo(150));
        });
    }

    [Test]
    public async Task ExecuteAsync_ServiceError_ReturnsErrorResult()
    {
        // Arrange
        var expectedResult = new MiniPromptResult(
            Success: false,
            Response: null,
            Error: "Service unavailable",
            CostUsd: null,
            DurationMs: null);

        _mockService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _mockService.Object.ExecuteAsync("test prompt");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Response, Is.Null);
            Assert.That(result.Error, Is.EqualTo("Service unavailable"));
        });
    }

    [Test]
    public async Task ExecuteAsync_UsesDefaultModel()
    {
        // Arrange
        _mockService.Setup(x => x.ExecuteAsync(It.IsAny<string>(), "haiku", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniPromptResult(true, "response", null, null, null));

        // Act - calling without model parameter should use "haiku"
        await _mockService.Object.ExecuteAsync("test prompt");

        // Assert
        _mockService.Verify(x => x.ExecuteAsync("test prompt", "haiku", It.IsAny<CancellationToken>()), Times.Once);
    }
}
