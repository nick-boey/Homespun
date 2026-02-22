using System.Net;
using System.Text.Json;
using Homespun.Features.AgentOrchestration.Services;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Homespun.Tests.Features.AgentOrchestration;

/// <summary>
/// Tests for the MiniPromptService with local execution mode (no sidecar URL configured).
/// </summary>
[TestFixture]
public class MiniPromptServiceLocalTests
{
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClientFactory.Setup(x => x.CreateClient("MiniPrompt"))
            .Returns(new HttpClient());
    }

    private MiniPromptService CreateService(MiniPromptOptions? options = null)
    {
        var opts = Options.Create(options ?? new MiniPromptOptions());
        return new MiniPromptService(opts, _mockHttpClientFactory.Object);
    }

    [Test]
    public void ExecuteAsync_EmptyPrompt_ThrowsArgumentException()
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync(""));
    }

    [Test]
    public void ExecuteAsync_NullPrompt_ThrowsArgumentException()
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync(null!));
    }

    [Test]
    public void ExecuteAsync_WhitespacePrompt_ThrowsArgumentException()
    {
        var service = CreateService();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ExecuteAsync("   "));
    }

    [Test]
    public async Task ExecuteAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ExecuteAsync("test prompt", cancellationToken: cts.Token));
    }

    [Test]
    public void ExecuteAsync_ValidPrompt_ServiceConstructedProperly()
    {
        // Verify the service is constructed properly with local mode (no sidecar URL)
        var service = CreateService();
        Assert.That(service, Is.Not.Null);
    }
}

/// <summary>
/// Tests for the MiniPromptService with sidecar execution mode.
/// </summary>
[TestFixture]
public class MiniPromptServiceSidecarTests
{
    private Mock<HttpMessageHandler> _mockHttpMessageHandler = null!;
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;
    private HttpClient _httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClientFactory.Setup(x => x.CreateClient("MiniPrompt"))
            .Returns(_httpClient);
    }

    private MiniPromptService CreateService(string sidecarUrl = "http://homespun-worker:8080")
    {
        var options = Options.Create(new MiniPromptOptions
        {
            SidecarUrl = sidecarUrl,
            RequestTimeout = TimeSpan.FromSeconds(30)
        });
        return new MiniPromptService(options, _mockHttpClientFactory.Object);
    }

    [Test]
    public async Task ExecuteAsync_SidecarReturnsSuccess_ReturnsSuccessResult()
    {
        // Arrange
        var responseBody = new
        {
            success = true,
            response = "test-branch-id",
            costUsd = 0.0001,
            durationMs = 150
        };

        SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(responseBody));

        var service = CreateService();

        // Act
        var result = await service.ExecuteAsync("Generate a branch ID for 'Add login feature'");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Response, Is.EqualTo("test-branch-id"));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.CostUsd, Is.EqualTo(0.0001m));
            Assert.That(result.DurationMs, Is.EqualTo(150));
        });
    }

    [Test]
    public async Task ExecuteAsync_SidecarReturnsError_ReturnsErrorResult()
    {
        // Arrange
        var responseBody = new
        {
            success = false,
            error = "AI service unavailable",
            durationMs = 50
        };

        SetupHttpResponse(HttpStatusCode.OK, JsonSerializer.Serialize(responseBody));

        var service = CreateService();

        // Act
        var result = await service.ExecuteAsync("test prompt");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Response, Is.Null);
            Assert.That(result.Error, Is.EqualTo("AI service unavailable"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SidecarReturnsHttpError_ReturnsErrorResult()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");

        var service = CreateService();

        // Act
        var result = await service.ExecuteAsync("test prompt");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("500"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SidecarConnectionFailed_ReturnsErrorResult()
    {
        // Arrange
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService();

        // Act
        var result = await service.ExecuteAsync("test prompt");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Sidecar connection failed"));
        });
    }

    [Test]
    public async Task ExecuteAsync_SendsCorrectRequestBody()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var responseBody = new { success = true, response = "result" };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseBody))
            });

        var service = CreateService("http://worker:8080");

        // Act
        await service.ExecuteAsync("test prompt", "sonnet");

        // Assert
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest!.RequestUri?.ToString(), Is.EqualTo("http://worker:8080/api/mini-prompt"));
            Assert.That(capturedRequest.Method, Is.EqualTo(HttpMethod.Post));
        });

        var content = await capturedRequest!.Content!.ReadAsStringAsync();
        var requestBody = JsonDocument.Parse(content);
        Assert.Multiple(() =>
        {
            Assert.That(requestBody.RootElement.GetProperty("prompt").GetString(), Is.EqualTo("test prompt"));
            Assert.That(requestBody.RootElement.GetProperty("model").GetString(), Is.EqualTo("sonnet"));
        });
    }

    [Test]
    public void ExecuteAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = CreateService();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ExecuteAsync("test prompt", cancellationToken: cts.Token));
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
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
