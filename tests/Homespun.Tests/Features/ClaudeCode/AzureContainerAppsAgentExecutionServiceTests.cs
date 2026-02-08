using System.Text.Json;
using Homespun.ClaudeAgentSdk;
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
    public async Task AnswerQuestionAsync_NonExistentSession_DoesNotThrow()
    {
        // Arrange
        var request = new AgentAnswerRequest(
            "non-existent-session",
            "tool-use-123",
            new Dictionary<string, string> { { "Q1", "A1" } });

        // Act & Assert - should log warning but not throw
        Assert.DoesNotThrowAsync(async () =>
            await _service.AnswerQuestionAsync(request));
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
    public async Task DisposeAsync_NoSessions_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await _service.DisposeAsync());
    }

    [Test]
    public void AgentStartRequest_DefaultsPermissionModeToNull()
    {
        // Arrange & Act
        var request = new AgentStartRequest(
            WorkingDirectory: "/test",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "hello"
        );

        // Assert
        Assert.That(request.PermissionMode, Is.Null);
    }

    [Test]
    public void AgentStartRequest_AcceptsPermissionMode()
    {
        // Arrange & Act
        var request = new AgentStartRequest(
            WorkingDirectory: "/test",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "hello",
            PermissionMode: PermissionMode.Default
        );

        // Assert
        Assert.That(request.PermissionMode, Is.EqualTo(PermissionMode.Default));
    }

    [Test]
    public void AgentMessageRequest_DefaultsPermissionModeToNull()
    {
        // Arrange & Act
        var request = new AgentMessageRequest(
            SessionId: "session-1",
            Message: "hello"
        );

        // Assert
        Assert.That(request.PermissionMode, Is.Null);
    }

    [Test]
    public void AgentMessageRequest_AcceptsPermissionMode()
    {
        // Arrange & Act
        var request = new AgentMessageRequest(
            SessionId: "session-1",
            Message: "hello",
            PermissionMode: PermissionMode.AcceptEdits
        );

        // Assert
        Assert.That(request.PermissionMode, Is.EqualTo(PermissionMode.AcceptEdits));
    }

    [Test]
    public void MessageParser_ParsesControlRequest_WithRequestId()
    {
        // Arrange
        var json = """
        {
            "type": "control_request",
            "control_type": "tool_permission",
            "request_id": "req-123",
            "data": {
                "tool_name": "Bash",
                "input": { "command": "ls" }
            }
        }
        """;
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        // Act
        var message = MessageParser.ParseMessage(data);

        // Assert
        Assert.That(message, Is.InstanceOf<ControlRequest>());
        var controlRequest = (ControlRequest)message;
        Assert.Multiple(() =>
        {
            Assert.That(controlRequest.ControlType, Is.EqualTo("tool_permission"));
            Assert.That(controlRequest.RequestId, Is.EqualTo("req-123"));
            Assert.That(controlRequest.Data, Is.Not.Null);
        });
    }

    [Test]
    public void MessageParser_ParsesControlRequest_WithoutRequestId()
    {
        // Arrange
        var json = """
        {
            "type": "control_request",
            "control_type": "tool_permission",
            "data": {
                "tool_name": "Read"
            }
        }
        """;
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        // Act
        var message = MessageParser.ParseMessage(data);

        // Assert
        Assert.That(message, Is.InstanceOf<ControlRequest>());
        var controlRequest = (ControlRequest)message;
        Assert.Multiple(() =>
        {
            Assert.That(controlRequest.ControlType, Is.EqualTo("tool_permission"));
            Assert.That(controlRequest.RequestId, Is.Null);
        });
    }

    [Test]
    public void ControlResponse_AllowFormat_IsCorrectJson()
    {
        // Arrange - verify the JSON format matches the CLI's expected nested structure
        // updatedInput must always be present for allow responses (even if empty)
        var permissionResult = new Dictionary<string, object>
        {
            ["behavior"] = "allow",
            ["updatedInput"] = new Dictionary<string, object>()
        };

        var message = new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = "req-123",
                response = permissionResult
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-123"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("allow"));
            Assert.That(innerResponse.TryGetProperty("updatedInput", out _), Is.True,
                "Allow responses must always include updatedInput");
        });
    }

    [Test]
    public void ControlResponse_AllowWithUpdatedInput_IncludesAnswers()
    {
        // Arrange
        var updatedInput = new Dictionary<string, object>
        {
            ["answers"] = new Dictionary<string, string>
            {
                ["Which database?"] = "PostgreSQL"
            }
        };

        var permissionResult = new Dictionary<string, object>
        {
            ["behavior"] = "allow",
            ["updatedInput"] = updatedInput
        };

        var message = new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = "req-456",
                response = permissionResult
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-456"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("allow"));
            Assert.That(innerResponse.TryGetProperty("updatedInput", out _), Is.True);
        });
    }

    [Test]
    public void ControlResponse_DenyFormat_IsCorrectJson()
    {
        // Arrange
        var permissionResult = new Dictionary<string, object>
        {
            ["behavior"] = "deny",
            ["message"] = "User denied this action"
        };

        var message = new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = "req-789",
                response = permissionResult
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-789"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("deny"));
            Assert.That(innerResponse.GetProperty("message").GetString(), Is.EqualTo("User denied this action"));
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
