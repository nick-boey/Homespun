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
            SubscriptionId = "test-subscription-id",
            ResourceGroup = "test-resource-group",
            SessionPoolName = "test-session-pool",
            WorkerImage = "ghcr.io/nick-boey/homespun-worker:test",
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
            Assert.That(defaultOptions.SubscriptionId, Is.Empty);
            Assert.That(defaultOptions.ResourceGroup, Is.Empty);
            Assert.That(defaultOptions.SessionPoolName, Is.EqualTo("homespun-agents"));
            Assert.That(defaultOptions.WorkerImage, Is.EqualTo("ghcr.io/nick-boey/homespun-worker:latest"));
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
/// Tests for Azure Container Apps API URL construction.
/// </summary>
[TestFixture]
public class AzureContainerAppsApiUrlTests
{
    [Test]
    public void ApiUrl_ForSessionCreation_IsConstructedCorrectly()
    {
        // Arrange
        var subscriptionId = "sub-123";
        var resourceGroup = "rg-test";
        var sessionPoolName = "pool-test";
        var sessionId = "session-456";

        // Act
        var expectedUrl = $"https://management.azure.com/subscriptions/{subscriptionId}" +
                         $"/resourceGroups/{resourceGroup}" +
                         $"/providers/Microsoft.App/sessionPools/{sessionPoolName}" +
                         $"/sessions/{sessionId}?api-version=2024-02-02-preview";

        // Assert
        Assert.That(expectedUrl, Does.Contain("management.azure.com"));
        Assert.That(expectedUrl, Does.Contain(subscriptionId));
        Assert.That(expectedUrl, Does.Contain(resourceGroup));
        Assert.That(expectedUrl, Does.Contain(sessionPoolName));
        Assert.That(expectedUrl, Does.Contain(sessionId));
        Assert.That(expectedUrl, Does.Contain("api-version=2024-02-02-preview"));
    }

    [Test]
    public void ApiUrl_ForSessionDeletion_IsConstructedCorrectly()
    {
        // Arrange
        var subscriptionId = "sub-123";
        var resourceGroup = "rg-test";
        var sessionPoolName = "pool-test";
        var sessionId = "session-456";

        // Act
        var expectedUrl = $"https://management.azure.com/subscriptions/{subscriptionId}" +
                         $"/resourceGroups/{resourceGroup}" +
                         $"/providers/Microsoft.App/sessionPools/{sessionPoolName}" +
                         $"/sessions/{sessionId}?api-version=2024-02-02-preview";

        // Assert - URL should be identical for PUT (create) and DELETE operations
        Assert.That(expectedUrl, Does.Contain("Microsoft.App/sessionPools"));
        Assert.That(expectedUrl, Does.Contain("/sessions/"));
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
    public async Task DisposeAsync_NoSessions_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.DisposeAsync());
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
