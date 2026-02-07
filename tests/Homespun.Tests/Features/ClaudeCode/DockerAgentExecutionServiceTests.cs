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

    [Test]
    public void AgentAnswerRequest_Properties_AreSetCorrectly()
    {
        // Arrange
        var answers = new Dictionary<string, string>
        {
            { "Question 1", "Answer 1" },
            { "Question 2", "Answer 2" }
        };

        // Act
        var request = new AgentAnswerRequest(
            SessionId: "session-123",
            ToolUseId: "tool-use-456",
            Answers: answers);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.SessionId, Is.EqualTo("session-123"));
            Assert.That(request.ToolUseId, Is.EqualTo("tool-use-456"));
            Assert.That(request.Answers, Has.Count.EqualTo(2));
            Assert.That(request.Answers["Question 1"], Is.EqualTo("Answer 1"));
        });
    }
}

/// <summary>
/// Tests for agent event types.
/// </summary>
[TestFixture]
public class AgentEventTests
{
    [Test]
    public void AgentSessionStartedEvent_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentSessionStartedEvent("session-123", "conversation-456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.ConversationId, Is.EqualTo("conversation-456"));
        });
    }

    [Test]
    public void AgentContentBlockEvent_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentContentBlockEvent(
            SessionId: "session-123",
            Type: ClaudeContentType.Text,
            Text: "Hello world",
            ToolName: null,
            ToolInput: null,
            ToolUseId: null,
            ToolSuccess: null,
            Index: 0);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.Type, Is.EqualTo(ClaudeContentType.Text));
            Assert.That(evt.Text, Is.EqualTo("Hello world"));
            Assert.That(evt.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void AgentContentBlockEvent_ToolUse_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentContentBlockEvent(
            SessionId: "session-123",
            Type: ClaudeContentType.ToolUse,
            Text: null,
            ToolName: "Read",
            ToolInput: "{\"file_path\": \"/test.txt\"}",
            ToolUseId: "tool-use-789",
            ToolSuccess: null,
            Index: 1);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.Type, Is.EqualTo(ClaudeContentType.ToolUse));
            Assert.That(evt.ToolName, Is.EqualTo("Read"));
            Assert.That(evt.ToolInput, Is.EqualTo("{\"file_path\": \"/test.txt\"}"));
            Assert.That(evt.ToolUseId, Is.EqualTo("tool-use-789"));
        });
    }

    [Test]
    public void AgentResultEvent_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentResultEvent(
            SessionId: "session-123",
            TotalCostUsd: 0.05m,
            DurationMs: 5000,
            ConversationId: "conversation-456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.TotalCostUsd, Is.EqualTo(0.05m));
            Assert.That(evt.DurationMs, Is.EqualTo(5000));
            Assert.That(evt.ConversationId, Is.EqualTo("conversation-456"));
        });
    }

    [Test]
    public void AgentQuestionEvent_Properties_AreSetCorrectly()
    {
        // Arrange
        var questions = new List<AgentQuestion>
        {
            new AgentQuestion(
                Question: "Which option?",
                Header: "Choice",
                Options: new List<AgentQuestionOption>
                {
                    new AgentQuestionOption("Option A", "Description A"),
                    new AgentQuestionOption("Option B", "Description B")
                },
                MultiSelect: false)
        };

        // Act
        var evt = new AgentQuestionEvent(
            SessionId: "session-123",
            QuestionId: "question-456",
            ToolUseId: "tool-789",
            Questions: questions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.QuestionId, Is.EqualTo("question-456"));
            Assert.That(evt.ToolUseId, Is.EqualTo("tool-789"));
            Assert.That(evt.Questions, Has.Count.EqualTo(1));
            Assert.That(evt.Questions[0].Question, Is.EqualTo("Which option?"));
            Assert.That(evt.Questions[0].Options, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AgentSessionEndedEvent_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentSessionEndedEvent(
            SessionId: "session-123",
            Reason: "completed");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.Reason, Is.EqualTo("completed"));
        });
    }

    [Test]
    public void AgentErrorEvent_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var evt = new AgentErrorEvent(
            SessionId: "session-123",
            Message: "Something went wrong",
            Code: "ERROR_CODE",
            IsRecoverable: true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.Message, Is.EqualTo("Something went wrong"));
            Assert.That(evt.Code, Is.EqualTo("ERROR_CODE"));
            Assert.That(evt.IsRecoverable, Is.True);
        });
    }

    [Test]
    public void AgentMessageEvent_Properties_AreSetCorrectly()
    {
        // Arrange
        var content = new List<AgentContentBlockEvent>
        {
            new AgentContentBlockEvent(
                "session-123",
                ClaudeContentType.Text,
                "Hello",
                null, null, null, null, 0)
        };

        // Act
        var evt = new AgentMessageEvent(
            SessionId: "session-123",
            Role: ClaudeMessageRole.Assistant,
            Content: content);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.SessionId, Is.EqualTo("session-123"));
            Assert.That(evt.Role, Is.EqualTo(ClaudeMessageRole.Assistant));
            Assert.That(evt.Content, Has.Count.EqualTo(1));
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
