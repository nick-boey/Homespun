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
            Assert.That(defaultOptions.ProjectsBasePath, Is.EqualTo("projects"));
        });
    }

    [Test]
    public void Options_SectionName_IsCorrect()
    {
        Assert.That(DockerAgentExecutionOptions.SectionName, Is.EqualTo("AgentExecution:Docker"));
    }

    #endregion

    #region Container Naming Tests

    [Test]
    public void GetIssueContainerName_ReturnsCorrectFormat()
    {
        var name = DockerAgentExecutionService.GetIssueContainerName("abc123");
        Assert.That(name, Is.EqualTo("homespun-issue-abc123"));
    }

    [Test]
    public void GetIssueContainerName_IsDeterministic()
    {
        var name1 = DockerAgentExecutionService.GetIssueContainerName("abc123");
        var name2 = DockerAgentExecutionService.GetIssueContainerName("abc123");
        Assert.That(name1, Is.EqualTo(name2));
    }

    [Test]
    public void GetIssueContainerName_DifferentIssues_ReturnDifferentNames()
    {
        var name1 = DockerAgentExecutionService.GetIssueContainerName("issue-1");
        var name2 = DockerAgentExecutionService.GetIssueContainerName("issue-2");
        Assert.That(name1, Is.Not.EqualTo(name2));
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

    #region ParseWorkerIpAddress Tests

    [Test]
    public void ParseWorkerIpAddress_ValidIp_ReturnsIp()
    {
        var result = DockerAgentExecutionService.ParseWorkerIpAddress("172.17.0.3");
        Assert.That(result, Is.EqualTo("172.17.0.3"));
    }

    [Test]
    public void ParseWorkerIpAddress_ValidIpWithWhitespace_ReturnsTrimmedIp()
    {
        var result = DockerAgentExecutionService.ParseWorkerIpAddress("  172.17.0.3\n");
        Assert.That(result, Is.EqualTo("172.17.0.3"));
    }

    [Test]
    public void ParseWorkerIpAddress_QuotedIp_ReturnsUnquotedIp()
    {
        var result = DockerAgentExecutionService.ParseWorkerIpAddress("\"172.17.0.3\"");
        Assert.That(result, Is.EqualTo("172.17.0.3"));
    }

    [Test]
    public void ParseWorkerIpAddress_SingleQuotedIp_ReturnsUnquotedIp()
    {
        var result = DockerAgentExecutionService.ParseWorkerIpAddress("'172.17.0.3'");
        Assert.That(result, Is.EqualTo("172.17.0.3"));
    }

    [Test]
    public void ParseWorkerIpAddress_ConcatenatedIps_ReturnsFirstIp()
    {
        var result = DockerAgentExecutionService.ParseWorkerIpAddress("172.17.0.3172.18.0.4");
        Assert.That(result, Is.EqualTo("172.17.0.3"));
    }

    [Test]
    public void ParseWorkerIpAddress_NullInput_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress(null!),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_EmptyInput_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress(""),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_WhitespaceOnly_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress("   "),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_GoTemplateNilValue_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress("<no value>"),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_ErrorText_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress("Error: No such container"),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_GarbageText_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress("not-an-ip-address"),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void ParseWorkerIpAddress_PartialIp_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.ParseWorkerIpAddress("172.17"),
            Throws.InstanceOf<AgentStartupException>());
    }

    #endregion

    #region BuildWorkerUrl Tests

    [Test]
    public void BuildWorkerUrl_ValidIp_ReturnsHttpUrl()
    {
        var result = DockerAgentExecutionService.BuildWorkerUrl("172.17.0.3");
        Assert.That(result, Is.EqualTo("http://172.17.0.3:8080"));
    }

    [Test]
    public void BuildWorkerUrl_LocalhostIp_ReturnsHttpUrl()
    {
        var result = DockerAgentExecutionService.BuildWorkerUrl("127.0.0.1");
        Assert.That(result, Is.EqualTo("http://127.0.0.1:8080"));
    }

    [Test]
    public void BuildWorkerUrl_EmptyString_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.BuildWorkerUrl(""),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void BuildWorkerUrl_NullInput_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.BuildWorkerUrl(null!),
            Throws.InstanceOf<AgentStartupException>());
    }

    [Test]
    public void BuildWorkerUrl_MalformedInput_Throws()
    {
        Assert.That(() => DockerAgentExecutionService.BuildWorkerUrl("not:valid"),
            Throws.InstanceOf<AgentStartupException>());
    }

    #endregion

    #region BuildContainerDockerArgs Tests

    [Test]
    public void BuildContainerDockerArgs_MountsWorkingDirectoryToWorkdir()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = "/host/data",
            WorkerImage = "test-image:latest",
            NetworkName = "bridge"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options), _loggerMock.Object);

        // Act
        var args = service.BuildContainerDockerArgs(
            "test-container", "/data/src/Homespun/.clones/my-branch", useRm: false);

        // Assert - working directory is mounted to /workdir with host path translation
        Assert.That(args, Does.Contain("-v \"/host/data/src/Homespun/.clones/my-branch:/workdir\""));
    }

    [Test]
    public void BuildContainerDockerArgs_SetsWorkingDirectoryEnvVar()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert
        Assert.That(args, Does.Contain("-e WORKING_DIRECTORY=/workdir"));
    }

    [Test]
    public void BuildContainerDockerArgs_DoesNotMountFullDataVolume()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert - should NOT have the full /data volume mount
        Assert.That(args, Does.Not.Contain($"-v \"/data:/data\""));
    }

    [Test]
    public void BuildContainerDockerArgs_IncludesRmFlag_WhenUseRmIsTrue()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: true);

        // Assert
        Assert.That(args, Does.Contain("run -d --rm"));
    }

    [Test]
    public void BuildContainerDockerArgs_ExcludesRmFlag_WhenUseRmIsFalse()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert
        Assert.That(args, Does.Contain("run -d "));
        Assert.That(args, Does.Not.Contain("--rm"));
    }

    [Test]
    public void BuildContainerDockerArgs_IncludesIssueEnvVars_WhenProvided()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false,
            issueId: "abc123", projectName: "my-project");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-e ISSUE_ID=abc123"));
            Assert.That(args, Does.Contain("-e PROJECT_NAME=my-project"));
        });
    }

    [Test]
    public void BuildContainerDockerArgs_ExcludesIssueEnvVars_WhenNotProvided()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Not.Contain("ISSUE_ID"));
            Assert.That(args, Does.Not.Contain("PROJECT_NAME"));
        });
    }

    [Test]
    public void BuildContainerDockerArgs_IncludesContainerNameAndResourceLimits()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "my-container", "/data/some/path", useRm: false);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("--name my-container"));
            Assert.That(args, Does.Contain($"--memory {_options.MemoryLimitBytes}"));
            Assert.That(args, Does.Contain($"--cpus {_options.CpuLimit}"));
            Assert.That(args, Does.Contain($"--network {_options.NetworkName}"));
            Assert.That(args, Does.Contain(_options.WorkerImage));
        });
    }

    [Test]
    public void BuildContainerDockerArgs_DoesNotIncludeAspNetCoreUrls()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert
        Assert.That(args, Does.Not.Contain("ASPNETCORE_URLS"));
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
    public void AgentStartRequest_IssueFields_AreSetCorrectly()
    {
        // Arrange & Act
        var request = new AgentStartRequest(
            WorkingDirectory: "/workdir",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Test",
            IssueId: "abc123",
            ProjectId: "proj-1",
            ProjectName: "my-project");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.IssueId, Is.EqualTo("abc123"));
            Assert.That(request.ProjectId, Is.EqualTo("proj-1"));
            Assert.That(request.ProjectName, Is.EqualTo("my-project"));
        });
    }

    [Test]
    public void AgentStartRequest_IssueFields_DefaultToNull()
    {
        // Arrange & Act
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.IssueId, Is.Null);
            Assert.That(request.ProjectId, Is.Null);
            Assert.That(request.ProjectName, Is.Null);
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
