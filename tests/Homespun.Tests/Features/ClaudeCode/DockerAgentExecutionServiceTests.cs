using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for DockerAgentExecutionService.
/// These tests verify the service's behavior with mocked HTTP responses.
/// </summary>
[TestFixture]
public class DockerAgentExecutionServiceTests
{
    private DockerAgentExecutionService _service = null!;
    private Mock<ILogger<DockerAgentExecutionService>> _loggerMock = null!;
    private DockerAgentExecutionOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<DockerAgentExecutionService>>();
        _options = new DockerAgentExecutionOptions
        {
            WorkerImage = "ghcr.io/nick-boey/homespun-worker:test",
            DataVolumePath = "/data",
            MemoryLimitBytes = 4L * 1024 * 1024 * 1024,
            CpuLimit = 2.0,
            RequestTimeout = TimeSpan.FromSeconds(30),
            DockerSocketPath = "/var/run/docker.sock",
            NetworkName = "bridge"
        };

        _service = new DockerAgentExecutionService(
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
        var defaultOptions = new DockerAgentExecutionOptions();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(defaultOptions.WorkerImage, Is.EqualTo("ghcr.io/nick-boey/homespun-worker:latest"));
            Assert.That(defaultOptions.DataVolumePath, Is.EqualTo("/data"));
            Assert.That(defaultOptions.MemoryLimitBytes, Is.EqualTo(4L * 1024 * 1024 * 1024));
            Assert.That(defaultOptions.CpuLimit, Is.EqualTo(2.0));
            Assert.That(defaultOptions.RequestTimeout, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(defaultOptions.DockerSocketPath, Is.EqualTo("/var/run/docker.sock"));
            Assert.That(defaultOptions.NetworkName, Is.EqualTo("bridge"));
            Assert.That(defaultOptions.HostDataPath, Is.Null);
        });
    }

    [Test]
    public void Options_SectionName_IsCorrect()
    {
        Assert.That(DockerAgentExecutionOptions.SectionName, Is.EqualTo("AgentExecution:Docker"));
    }

    #endregion

    #region Path Translation Tests

    [Test]
    public void TranslateToHostPath_NoHostPath_ReturnsOriginal()
    {
        // Arrange - options without HostDataPath (default)
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = null
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options),
            _loggerMock.Object);

        // Act
        var result = service.TranslateToHostPath("/data/test-workspace");

        // Assert
        Assert.That(result, Is.EqualTo("/data/test-workspace"));
    }

    [Test]
    public void TranslateToHostPath_WithHostPath_TranslatesPath()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = "/home/azureuser/.homespun-container/data"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options),
            _loggerMock.Object);

        // Act
        var result = service.TranslateToHostPath("/data/test-workspace");

        // Assert
        Assert.That(result, Is.EqualTo("/home/azureuser/.homespun-container/data/test-workspace"));
    }

    [Test]
    public void TranslateToHostPath_PathNotUnderDataVolume_ReturnsOriginal()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = "/home/azureuser/.homespun-container/data"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options),
            _loggerMock.Object);

        // Act
        var result = service.TranslateToHostPath("/some/other/path");

        // Assert
        Assert.That(result, Is.EqualTo("/some/other/path"));
    }

    [Test]
    public void TranslateToHostPath_DataVolumePathItself_TranslatesCorrectly()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = "/home/azureuser/.homespun-container/data"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options),
            _loggerMock.Object);

        // Act
        var result = service.TranslateToHostPath("/data");

        // Assert
        Assert.That(result, Is.EqualTo("/home/azureuser/.homespun-container/data"));
    }

    [Test]
    public void TranslateToHostPath_NestedPath_TranslatesCorrectly()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = "/home/azureuser/.homespun-container/data"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options),
            _loggerMock.Object);

        // Act
        var result = service.TranslateToHostPath("/data/projects/feature-123/src");

        // Assert
        Assert.That(result, Is.EqualTo("/home/azureuser/.homespun-container/data/projects/feature-123/src"));
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
/// Tests for DockerSession record behavior.
/// </summary>
[TestFixture]
public class DockerSessionRecordTests
{
    [Test]
    public void AgentStartRequest_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "claude-sonnet-4-20250514",
            Prompt: "Test prompt",
            SystemPrompt: "System prompt",
            ResumeSessionId: "resume-123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.WorkingDirectory, Is.EqualTo("/test/path"));
            Assert.That(request.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(request.Model, Is.EqualTo("claude-sonnet-4-20250514"));
            Assert.That(request.Prompt, Is.EqualTo("Test prompt"));
            Assert.That(request.SystemPrompt, Is.EqualTo("System prompt"));
            Assert.That(request.ResumeSessionId, Is.EqualTo("resume-123"));
        });
    }

    [Test]
    public void AgentMessageRequest_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var request = new AgentMessageRequest(
            SessionId: "session-123",
            Message: "Hello",
            Model: "claude-opus-4");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.SessionId, Is.EqualTo("session-123"));
            Assert.That(request.Message, Is.EqualTo("Hello"));
            Assert.That(request.Model, Is.EqualTo("claude-opus-4"));
        });
    }

}


/// <summary>
/// Tests for SdkMessage record types.
/// </summary>
[TestFixture]
public class SdkMessageRecordTests
{
    [Test]
    public void SdkSystemMessage_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var msg = new SdkSystemMessage("session-123", null, "session_started", "sonnet", null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("session-123"));
            Assert.That(msg.Type, Is.EqualTo("system"));
            Assert.That(msg.Subtype, Is.EqualTo("session_started"));
            Assert.That(msg.Model, Is.EqualTo("sonnet"));
        });
    }

    [Test]
    public void SdkResultMessage_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var msg = new SdkResultMessage(
            SessionId: "conversation-456",
            Uuid: null,
            Subtype: null,
            DurationMs: 5000,
            DurationApiMs: 3000,
            IsError: false,
            NumTurns: 3,
            TotalCostUsd: 0.05m,
            Result: null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("conversation-456"));
            Assert.That(msg.Type, Is.EqualTo("result"));
            Assert.That(msg.TotalCostUsd, Is.EqualTo(0.05m));
            Assert.That(msg.DurationMs, Is.EqualTo(5000));
        });
    }

    [Test]
    public void SdkAssistantMessage_Properties_AreSetCorrectly()
    {
        // Arrange
        var content = new List<SdkContentBlock>
        {
            new SdkTextBlock("Hello world")
        };
        var apiMessage = new SdkApiMessage("assistant", content);

        // Act
        var msg = new SdkAssistantMessage("session-123", null, apiMessage, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("session-123"));
            Assert.That(msg.Type, Is.EqualTo("assistant"));
            Assert.That(msg.Message.Role, Is.EqualTo("assistant"));
            Assert.That(msg.Message.Content, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SdkUserMessage_Properties_AreSetCorrectly()
    {
        // Arrange
        var content = new List<SdkContentBlock>
        {
            new SdkToolResultBlock("tool-use-1", default, null)
        };
        var apiMessage = new SdkApiMessage("user", content);

        // Act
        var msg = new SdkUserMessage("session-123", null, apiMessage, null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("session-123"));
            Assert.That(msg.Type, Is.EqualTo("user"));
            Assert.That(msg.Message.Role, Is.EqualTo("user"));
            Assert.That(msg.Message.Content, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AgentSessionStatus_Properties_AreSetCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var status = new AgentSessionStatus(
            SessionId: "session-123",
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "claude-sonnet-4-20250514",
            ConversationId: "conversation-456",
            CreatedAt: now.AddHours(-1),
            LastActivityAt: now);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(status.SessionId, Is.EqualTo("session-123"));
            Assert.That(status.WorkingDirectory, Is.EqualTo("/test/path"));
            Assert.That(status.Mode, Is.EqualTo(SessionMode.Build));
            Assert.That(status.Model, Is.EqualTo("claude-sonnet-4-20250514"));
            Assert.That(status.ConversationId, Is.EqualTo("conversation-456"));
            Assert.That(status.CreatedAt, Is.EqualTo(now.AddHours(-1)));
            Assert.That(status.LastActivityAt, Is.EqualTo(now));
        });
    }
}
