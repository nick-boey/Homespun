using System.Net;
using System.Net.Http.Json;
using Homespun.Shared.Models.Observability;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the ClientTelemetry API endpoints.
/// </summary>
[TestFixture]
public class ClientTelemetryApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task PostClientTelemetry_WithValidBatch_Returns202Accepted()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session-123",
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.PageView,
                    Name = "/projects",
                    Timestamp = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clienttelemetry", batch);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task PostClientTelemetry_WithEmptyBatch_Returns400BadRequest()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session-123",
            Events = []
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clienttelemetry", batch);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostClientTelemetry_WithMultipleEventTypes_Returns202Accepted()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = "test-session-123",
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.PageView,
                    Name = "/projects",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Event,
                    Name = "ButtonClicked",
                    Timestamp = DateTimeOffset.UtcNow,
                    Properties = new Dictionary<string, string> { ["buttonId"] = "submit" }
                },
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Exception,
                    Name = "NullReferenceException",
                    Timestamp = DateTimeOffset.UtcNow,
                    Properties = new Dictionary<string, string> { ["message"] = "Object reference not set" }
                },
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.Dependency,
                    Name = "GET /api/projects",
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = 150.5,
                    Success = true,
                    StatusCode = 200
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clienttelemetry", batch);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task PostClientTelemetry_WithNullSessionId_Returns202Accepted()
    {
        // Arrange
        var batch = new ClientTelemetryBatch
        {
            SessionId = null,
            Events =
            [
                new ClientTelemetryEvent
                {
                    Type = TelemetryEventType.PageView,
                    Name = "/projects",
                    Timestamp = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/clienttelemetry", batch);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }
}
