using Homespun.Features.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class GitHubApiHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ApiReachable_ReturnsHealthy()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new GitHubApiHealthCheck(mockFactory.Object);

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task CheckHealthAsync_ApiReturnsError_ReturnsDegraded()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new GitHubApiHealthCheck(mockFactory.Object);

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task CheckHealthAsync_ApiUnreachable_ReturnsDegraded()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            new HttpRequestException("Network unreachable"));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new GitHubApiHealthCheck(mockFactory.Object);

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
        Assert.That(result.Description, Does.Contain("unreachable"));
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public TestHttpMessageHandler(HttpResponseMessage response) => _response = response;
        public TestHttpMessageHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_exception is not null)
                throw _exception;

            return Task.FromResult(_response!);
        }
    }
}
