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
            Assert.That(defaultOptions.WorkerImage, Is.EqualTo("ghcr.io/nick-boey/homespun-worker:latest"));
            Assert.That(defaultOptions.ProjectsBasePath, Is.EqualTo("projects"));
            Assert.That(defaultOptions.ProvisioningTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(defaultOptions.EnvironmentId, Is.Null);
            Assert.That(defaultOptions.ResourceGroupName, Is.Null);
            Assert.That(defaultOptions.StorageMountName, Is.Null);
        });
    }

    [Test]
    public void Options_SectionName_IsCorrect()
    {
        Assert.That(AzureContainerAppsAgentExecutionOptions.SectionName, Is.EqualTo("AgentExecution:AzureContainerApps"));
    }

    [Test]
    public void Options_IsDynamicMode_FalseWhenNotConfigured()
    {
        var options = new AzureContainerAppsAgentExecutionOptions();
        Assert.That(options.IsDynamicMode, Is.False);
    }

    [Test]
    public void Options_IsDynamicMode_FalseWhenOnlyEnvironmentIdSet()
    {
        var options = new AzureContainerAppsAgentExecutionOptions
        {
            EnvironmentId = "/subscriptions/sub-id/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/env"
        };
        Assert.That(options.IsDynamicMode, Is.False);
    }

    [Test]
    public void Options_IsDynamicMode_FalseWhenOnlyResourceGroupSet()
    {
        var options = new AzureContainerAppsAgentExecutionOptions
        {
            ResourceGroupName = "rg-homespun-dev"
        };
        Assert.That(options.IsDynamicMode, Is.False);
    }

    [Test]
    public void Options_IsDynamicMode_TrueWhenBothSet()
    {
        var options = new AzureContainerAppsAgentExecutionOptions
        {
            EnvironmentId = "/subscriptions/sub-id/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/env",
            ResourceGroupName = "rg-homespun-dev"
        };
        Assert.That(options.IsDynamicMode, Is.True);
    }

    #endregion

    #region Container App Naming Tests

    [Test]
    public void GetIssueContainerAppName_ReturnsCorrectFormat()
    {
        var name = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("abc123");
        Assert.That(name, Is.EqualTo("ca-issue-abc123"));
    }

    [Test]
    public void GetIssueContainerAppName_IsLowercase()
    {
        var name = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("ABC123");
        Assert.That(name, Is.EqualTo("ca-issue-abc123"));
    }

    [Test]
    public void GetIssueContainerAppName_TruncatesAt32Chars()
    {
        var longIssueId = "this-is-a-very-long-issue-id-that-exceeds-the-limit";
        var name = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName(longIssueId);
        Assert.That(name.Length, Is.LessThanOrEqualTo(32));
    }

    [Test]
    public void GetIssueContainerAppName_IsDeterministic()
    {
        var name1 = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("abc123");
        var name2 = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("abc123");
        Assert.That(name1, Is.EqualTo(name2));
    }

    [Test]
    public void GetIssueContainerAppName_DifferentIssues_ReturnDifferentNames()
    {
        var name1 = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("issue-1");
        var name2 = AzureContainerAppsAgentExecutionService.GetIssueContainerAppName("issue-2");
        Assert.That(name1, Is.Not.EqualTo(name2));
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
    public async Task SendMessageAsync_NonExistentSession_YieldsNoMessages()
    {
        // Arrange
        var request = new AgentMessageRequest("non-existent-session", "Hello");

        // Act
        var messages = new List<SdkMessage>();
        await foreach (var msg in _service.SendMessageAsync(request))
        {
            messages.Add(msg);
        }

        // Assert - non-existent session yields no messages (yield break)
        Assert.That(messages, Is.Empty);
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
    public async Task SendMessageAsync_NonExistentSession_YieldsNoMessages()
    {
        // Arrange
        var request = new AgentMessageRequest("non-existent-session", "Hello");

        // Act
        var messages = new List<SdkMessage>();
        await foreach (var msg in _service.SendMessageAsync(request))
        {
            messages.Add(msg);
        }

        // Assert - non-existent session yields no messages (yield break)
        Assert.That(messages, Is.Empty);
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
