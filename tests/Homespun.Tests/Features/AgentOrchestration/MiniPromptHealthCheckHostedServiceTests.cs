using System.Net;
using Homespun.Features.AgentOrchestration.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Homespun.Tests.Features.AgentOrchestration;

/// <summary>
/// Tests for the startup probe of the mini-prompt sidecar.
/// </summary>
[TestFixture]
public class MiniPromptHealthCheckHostedServiceTests
{
    private Mock<HttpMessageHandler> _handler = null!;
    private Mock<IHttpClientFactory> _clientFactory = null!;
    private Mock<ILogger<MiniPromptHealthCheckHostedService>> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        _clientFactory = new Mock<IHttpClientFactory>();
        _clientFactory
            .Setup(x => x.CreateClient("MiniPrompt"))
            .Returns(() => new HttpClient(_handler.Object));
        _logger = new Mock<ILogger<MiniPromptHealthCheckHostedService>>();
    }

    private MiniPromptHealthCheckHostedService Create(string? sidecarUrl)
    {
        var options = Options.Create(new MiniPromptOptions { SidecarUrl = sidecarUrl });
        return new MiniPromptHealthCheckHostedService(options, _clientFactory.Object, _logger.Object);
    }

    [Test]
    public async Task StartAsync_NoSidecarConfigured_LogsWarningAndDoesNotProbe()
    {
        var service = Create(sidecarUrl: null);

        await service.StartAsync(CancellationToken.None);

        AssertLogged(LogLevel.Warning, "not configured");
        _handler.Protected().Verify(
            "SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_SidecarReachable_LogsInformationOnly()
    {
        // The sidecar route only accepts POST, so a GET probe returns 405 — that's fine,
        // it still proves the worker is up.
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        var service = Create("http://worker:8080");

        await service.StartAsync(CancellationToken.None);

        AssertLogged(LogLevel.Information, "reachable");
        AssertNotLogged(LogLevel.Warning);
    }

    [Test]
    public async Task StartAsync_SidecarUnreachable_LogsWarning()
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = Create("http://worker:8080");

        await service.StartAsync(CancellationToken.None);

        AssertLogged(LogLevel.Warning, "probe", "failed");
    }

    [Test]
    public async Task StartAsync_HostShutdown_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Simulate the probe respecting the cancellation token.
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, ct) =>
                Task.FromCanceled<HttpResponseMessage>(ct));

        var service = Create("http://worker:8080");

        Assert.CatchAsync<OperationCanceledException>(
            async () => await service.StartAsync(cts.Token));
    }

    private void AssertLogged(LogLevel level, params string[] expectedSubstrings)
    {
        _logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    expectedSubstrings.All(s =>
                        state!.ToString()!.Contains(s, StringComparison.OrdinalIgnoreCase))),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.AtLeastOnce,
            $"expected a {level} log containing [{string.Join(", ", expectedSubstrings)}]");
    }

    private void AssertNotLogged(LogLevel level)
    {
        _logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Never,
            $"expected no {level} log");
    }
}
