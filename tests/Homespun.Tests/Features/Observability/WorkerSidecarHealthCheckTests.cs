using Homespun.Features.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace Homespun.Tests.Features.Observability;

[TestFixture]
public class WorkerSidecarHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_NoSidecarUrl_ReturnsHealthy()
    {
        // Arrange
        var mockFactory = new Mock<IHttpClientFactory>();
        var check = new WorkerSidecarHealthCheck(mockFactory.Object, sidecarUrl: null);

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
        Assert.That(result.Description, Does.Contain("not configured"));
    }

    [Test]
    public async Task CheckHealthAsync_EmptySidecarUrl_ReturnsHealthy()
    {
        // Arrange
        var mockFactory = new Mock<IHttpClientFactory>();
        var check = new WorkerSidecarHealthCheck(mockFactory.Object, sidecarUrl: "");

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
    public async Task CheckHealthAsync_SidecarReachable_ReturnsHealthy()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new WorkerSidecarHealthCheck(mockFactory.Object, "http://worker:8080");

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
        Assert.That(result.Description, Does.Contain("reachable"));
    }

    [Test]
    public async Task CheckHealthAsync_SidecarReturnsError_ReturnsDegraded()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new WorkerSidecarHealthCheck(mockFactory.Object, "http://worker:8080");

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
    public async Task CheckHealthAsync_SidecarUnreachable_ReturnsUnhealthy()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(
            new HttpRequestException("Connection refused"));
        var client = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var check = new WorkerSidecarHealthCheck(mockFactory.Object, "http://worker:8080");

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", check, null, null)
            });

        // Assert
        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
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
