using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for AzureContainerAppsAgentExecutionService.
/// These tests verify the service's behavior with mocked HTTP responses.
/// </summary>
[TestFixture]
public class AzureContainerAppsAgentExecutionServiceTests
{
    private AzureContainerAppsAgentExecutionService _service = null!;
    private Mock<ILogger<AzureContainerAppsAgentExecutionService>> _loggerMock = null!;
    private AzureContainerAppsAgentExecutionOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<AzureContainerAppsAgentExecutionService>>();
        _options = new AzureContainerAppsAgentExecutionOptions
        {
            WorkerEndpoint = "http://test-worker.internal.azurecontainerapps.io",
            RequestTimeout = TimeSpan.FromSeconds(30),
            MaxSessionDuration = TimeSpan.FromMinutes(30)
        };

        _service = new AzureContainerAppsAgentExecutionService(
            Options.Create(_options),
            _loggerMock.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
    }

    #region Configuration Tests

    [Test]
    public void Options_DefaultValues_AreCorrect()
    {
        // Arrange
        var defaultOptions = new AzureContainerAppsAgentExecutionOptions();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(defaultOptions.WorkerEndpoint, Is.Empty);
            Assert.That(defaultOptions.RequestTimeout, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(defaultOptions.MaxSessionDuration, Is.EqualTo(TimeSpan.FromMinutes(30)));
        });
    }

    [Test]
    public void Options_SectionName_IsCorrect()
    {
        Assert.That(AzureContainerAppsAgentExecutionOptions.SectionName, Is.EqualTo("AgentExecution:AzureContainerApps"));
    }

    #endregion

    #region GetSessionStatusAsync Tests

    [Test]
    public async Task GetSessionStatusAsync_NonExistentSession_ReturnsNull()
    {
        // Act
        var result = await _service.GetSessionStatusAsync("non-existent-session");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region StopSessionAsync Tests

    [Test]
    public async Task StopSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopSessionAsync("non-existent-session"));
    }

    #endregion

    #region InterruptSessionAsync Tests

    [Test]
    public async Task InterruptSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.InterruptSessionAsync("non-existent-session"));
    }

    #endregion

    #region SendMessageAsync Tests

    [Test]
    public async Task SendMessageAsync_NonExistentSession_ReturnsError()
    {
        // Arrange
        var request = new AgentMessageRequest("non-existent-session", "Hello");

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in _service.SendMessageAsync(request))
        {
            events.Add(evt);
        }

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var errorEvent = events[0] as AgentErrorEvent;
        Assert.That(errorEvent, Is.Not.Null);
        Assert.That(errorEvent!.Code, Is.EqualTo("SESSION_NOT_FOUND"));
    }

    #endregion

    #region AnswerQuestionAsync Tests

    [Test]
    public async Task AnswerQuestionAsync_NonExistentSession_ReturnsError()
    {
        // Arrange
        var request = new AgentAnswerRequest(
            "non-existent-session",
            new Dictionary<string, string> { { "Q1", "A1" } });

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in _service.AnswerQuestionAsync(request))
        {
            events.Add(evt);
        }

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var errorEvent = events[0] as AgentErrorEvent;
        Assert.That(errorEvent, Is.Not.Null);
        Assert.That(errorEvent!.Code, Is.EqualTo("SESSION_NOT_FOUND"));
    }

    #endregion

    #region GetAllSessionsAsync Tests

    [Test]
    public async Task GetAllSessionsAsync_NoSessions_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetAllSessionsAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region GetOrphanedContainersAsync Tests

    [Test]
    public async Task GetOrphanedContainersAsync_ReturnsEmptyList()
    {
        // Azure Container Apps manages its own lifecycle
        // Act
        var result = await _service.GetOrphanedContainersAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region StopContainerByIdAsync Tests

    [Test]
    public async Task StopContainerByIdAsync_IsNoOp()
    {
        // Act & Assert - should be a no-op for ACA
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopContainerByIdAsync("some-container-id"));
    }

    #endregion

    #region DisposeAsync Tests

    [Test]
    public async Task DisposeAsync_NoSessions_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.DisposeAsync());
    }

    #endregion
}

/// <summary>
/// Tests for worker endpoint URL construction.
/// </summary>
[TestFixture]
public class WorkerEndpointUrlTests
{
    [Test]
    public void WorkerUrl_ForSessionStart_IsConstructedCorrectly()
    {
        // Arrange
        var workerEndpoint = "http://ca-worker-homespun-dev.internal.australiaeast.azurecontainerapps.io";

        // Act
        var url = $"{workerEndpoint.TrimEnd('/')}/api/sessions";

        // Assert
        Assert.That(url, Does.Contain("/api/sessions"));
        Assert.That(url, Does.StartWith("http://"));
        Assert.That(url, Does.Not.EndWith("//api/sessions"));
    }

    [Test]
    public void WorkerUrl_ForSessionMessage_IsConstructedCorrectly()
    {
        // Arrange
        var workerEndpoint = "http://ca-worker-homespun-dev.internal.australiaeast.azurecontainerapps.io";
        var workerSessionId = "session-456";

        // Act
        var url = $"{workerEndpoint.TrimEnd('/')}/api/sessions/{workerSessionId}/message";

        // Assert
        Assert.That(url, Does.Contain($"/api/sessions/{workerSessionId}/message"));
    }

    [Test]
    public void WorkerUrl_ForSessionDeletion_IsConstructedCorrectly()
    {
        // Arrange
        var workerEndpoint = "http://ca-worker-homespun-dev.internal.australiaeast.azurecontainerapps.io";
        var workerSessionId = "session-456";

        // Act
        var url = $"{workerEndpoint.TrimEnd('/')}/api/sessions/{workerSessionId}";

        // Assert
        Assert.That(url, Does.Contain($"/api/sessions/{workerSessionId}"));
        Assert.That(url, Does.Not.EndWith("/"));
    }

    [Test]
    public void WorkerUrl_WithTrailingSlash_IsHandledCorrectly()
    {
        // Arrange
        var workerEndpoint = "http://worker.internal.azurecontainerapps.io/";

        // Act
        var url = $"{workerEndpoint.TrimEnd('/')}/api/sessions";

        // Assert
        Assert.That(url, Does.Not.Contain("//api"));
    }
}

/// <summary>
/// Tests for LocalAgentExecutionService.
/// </summary>
[TestFixture]
public class LocalAgentExecutionServiceTests
{
    private LocalAgentExecutionService _service = null!;
    private SessionOptionsFactory _optionsFactory = null!;
    private Mock<ILogger<LocalAgentExecutionService>> _loggerMock = null!;
    private Mock<ILogger<SessionOptionsFactory>> _factoryLoggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        _optionsFactory = new SessionOptionsFactory(_factoryLoggerMock.Object);
        _loggerMock = new Mock<ILogger<LocalAgentExecutionService>>();

        _service = new LocalAgentExecutionService(
            _optionsFactory,
            _loggerMock.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
    }

    [Test]
    public async Task GetSessionStatusAsync_NonExistentSession_ReturnsNull()
    {
        // Act
        var result = await _service.GetSessionStatusAsync("non-existent-session");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task StopSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopSessionAsync("non-existent-session"));
    }

    [Test]
    public async Task InterruptSessionAsync_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.InterruptSessionAsync("non-existent-session"));
    }

    [Test]
    public async Task SendMessageAsync_NonExistentSession_ReturnsError()
    {
        // Arrange
        var request = new AgentMessageRequest("non-existent-session", "Hello");

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in _service.SendMessageAsync(request))
        {
            events.Add(evt);
        }

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        var errorEvent = events[0] as AgentErrorEvent;
        Assert.That(errorEvent, Is.Not.Null);
        Assert.That(errorEvent!.Code, Is.EqualTo("SESSION_NOT_FOUND"));
    }

    [Test]
    public async Task GetAllSessionsAsync_NoSessions_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetAllSessionsAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetOrphanedContainersAsync_ReturnsEmptyList()
    {
        // Local execution has no containers
        // Act
        var result = await _service.GetOrphanedContainersAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task StopContainerByIdAsync_IsNoOp()
    {
        // Act & Assert - should be a no-op for local execution
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopContainerByIdAsync("some-container-id"));
    }

    [Test]
    public async Task DisposeAsync_NoSessions_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.DisposeAsync());
    }
}

/// <summary>
/// Tests for OrphanedContainer record.
/// </summary>
[TestFixture]
public class OrphanedContainerTests
{
    [Test]
    public void OrphanedContainer_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var container = new OrphanedContainer(
            ContainerId: "abc123def456",
            ContainerName: "homespun-agent-abc12345",
            CreatedAt: "2025-01-15 10:30:00 +0000 UTC",
            Status: "Up 2 hours");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(container.ContainerId, Is.EqualTo("abc123def456"));
            Assert.That(container.ContainerName, Is.EqualTo("homespun-agent-abc12345"));
            Assert.That(container.CreatedAt, Is.EqualTo("2025-01-15 10:30:00 +0000 UTC"));
            Assert.That(container.Status, Is.EqualTo("Up 2 hours"));
        });
    }
}

/// <summary>
/// Tests for AgentExecutionOptions enum.
/// </summary>
[TestFixture]
public class AgentExecutionOptionsTests
{
    [Test]
    public void AgentExecutionMode_HasExpectedValues()
    {
        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(typeof(AgentExecutionMode), AgentExecutionMode.Local), Is.True);
            Assert.That(Enum.IsDefined(typeof(AgentExecutionMode), AgentExecutionMode.Docker), Is.True);
            Assert.That(Enum.IsDefined(typeof(AgentExecutionMode), AgentExecutionMode.AzureContainerApps), Is.True);
        });
    }

    [Test]
    public void AgentExecutionOptions_DefaultMode_IsLocal()
    {
        // Arrange
        var options = new AgentExecutionOptions();

        // Assert
        Assert.That(options.Mode, Is.EqualTo(AgentExecutionMode.Local));
    }

    [Test]
    public void AgentExecutionOptions_DefaultMaxSessionDuration_Is30Minutes()
    {
        // Arrange
        var options = new AgentExecutionOptions();

        // Assert
        Assert.That(options.MaxSessionDuration, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void AgentExecutionOptions_SectionName_IsCorrect()
    {
        Assert.That(AgentExecutionOptions.SectionName, Is.EqualTo("AgentExecution"));
    }
}
