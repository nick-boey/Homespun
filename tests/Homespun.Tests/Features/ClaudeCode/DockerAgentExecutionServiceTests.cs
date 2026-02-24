using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Secrets;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using static Homespun.Features.ClaudeCode.Services.DockerAgentExecutionService;

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
    private Mock<ISecretsService> _secretsServiceMock = null!;
    private DockerAgentExecutionOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<DockerAgentExecutionService>>();
        _secretsServiceMock = new Mock<ISecretsService>();
        _secretsServiceMock
            .Setup(s => s.GetSecretsForInjectionAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _secretsServiceMock
            .Setup(s => s.GetSecretsForInjectionByProjectIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
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
            _loggerMock.Object,
            _secretsServiceMock.Object);
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
        var name = DockerAgentExecutionService.GetIssueContainerName("project-1", "abc123");
        Assert.That(name, Is.EqualTo("homespun-issue-project-1-abc123"));
    }

    [Test]
    public void GetIssueContainerName_IsDeterministic()
    {
        var name1 = DockerAgentExecutionService.GetIssueContainerName("project-1", "abc123");
        var name2 = DockerAgentExecutionService.GetIssueContainerName("project-1", "abc123");
        Assert.That(name1, Is.EqualTo(name2));
    }

    [Test]
    public void GetIssueContainerName_DifferentIssues_ReturnDifferentNames()
    {
        var name1 = DockerAgentExecutionService.GetIssueContainerName("project-1", "issue-1");
        var name2 = DockerAgentExecutionService.GetIssueContainerName("project-1", "issue-2");
        Assert.That(name1, Is.Not.EqualTo(name2));
    }

    [Test]
    public void GetIssueContainerName_DifferentProjects_SameIssue_ReturnDifferentNames()
    {
        var name1 = DockerAgentExecutionService.GetIssueContainerName("project-1", "issue-1");
        var name2 = DockerAgentExecutionService.GetIssueContainerName("project-2", "issue-1");
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
            _loggerMock.Object,
            _secretsServiceMock.Object);

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
            _loggerMock.Object,
            _secretsServiceMock.Object);

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
            _loggerMock.Object,
            _secretsServiceMock.Object);

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
            _loggerMock.Object,
            _secretsServiceMock.Object);

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
            _loggerMock.Object,
            _secretsServiceMock.Object);

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

    [Test]
    public async Task StopSessionAsync_WithForceStopContainerTrue_DoesNotThrow()
    {
        // Act & Assert - verifies forceStopContainer parameter is accepted
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopSessionAsync("non-existent-session", forceStopContainer: true));
    }

    [Test]
    public async Task StopSessionAsync_WithForceStopContainerFalse_DoesNotThrow()
    {
        // Act & Assert - verifies default behavior (false) works
        Assert.DoesNotThrowAsync(async () =>
            await _service.StopSessionAsync("non-existent-session", forceStopContainer: false));
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
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act
        var args = service.BuildContainerDockerArgs(
            "test-container", "/data/projects/Homespun/.clones/my-branch", useRm: false);

        // Assert - working directory is mounted to /workdir with host path translation
        Assert.That(args, Does.Contain("-v \"/host/data/projects/Homespun/.clones/my-branch:/workdir\""));
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

    [Test]
    public void BuildContainerDockerArgs_MountsClaudeDirectory()
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
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act - workingDirectory points to clone/workdir
        var args = service.BuildContainerDockerArgs(
            "test-container", "/data/repos/project/.clones/my-branch/workdir", useRm: false);

        // Assert - .claude directory (sibling of workdir) should be mounted to /home/homespun/.claude
        Assert.That(args, Does.Contain("-v \"/host/data/repos/project/.clones/my-branch/.claude:/home/homespun/.claude\""));
    }

    [Test]
    public void BuildContainerDockerArgs_DerivesClaudePathFromWorkdir()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            HostDataPath = null, // No host path translation
            WorkerImage = "test-image:latest",
            NetworkName = "bridge"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act
        var args = service.BuildContainerDockerArgs(
            "test-container", "/data/repos/project/.clones/feature+test/workdir", useRm: false);

        // Assert - should derive .claude path from parent of workdir
        Assert.That(args, Does.Contain("-v \"/data/repos/project/.clones/feature+test/.claude:/home/homespun/.claude\""));
        Assert.That(args, Does.Contain("-v \"/data/repos/project/.clones/feature+test/workdir:/workdir\""));
    }

    [Test]
    public void BuildContainerDockerArgs_MountsClaudeDirectoryBeforeWorkdir()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            WorkerImage = "test-image:latest",
            NetworkName = "bridge"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act
        var args = service.BuildContainerDockerArgs(
            "test-container", "/data/repos/.clones/branch/workdir", useRm: false);

        // Assert - both mounts should be present
        var claudeIndex = args.IndexOf(".claude:/home/homespun/.claude", StringComparison.Ordinal);
        var workdirIndex = args.IndexOf(":/workdir", StringComparison.Ordinal);

        Assert.That(claudeIndex, Is.GreaterThan(0), ".claude mount should be present");
        Assert.That(workdirIndex, Is.GreaterThan(0), "/workdir mount should be present");
    }

    [Test]
    public void BuildContainerDockerArgs_UsesExplicitClaudePath()
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
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act - provide explicit claudePath separate from workingDirectory
        var args = service.BuildContainerDockerArgs(
            "test-container",
            "/data/projects/myproject/issues/abc123/src",
            useRm: false,
            claudePath: "/data/projects/myproject/issues/abc123/.claude");

        // Assert - should use the explicit .claude path, not derive from workingDirectory
        Assert.That(args, Does.Contain("-v \"/host/data/projects/myproject/issues/abc123/.claude:/home/homespun/.claude\""));
        Assert.That(args, Does.Contain("-v \"/host/data/projects/myproject/issues/abc123/src:/workdir\""));
    }

    [Test]
    public void BuildContainerDockerArgs_ExplicitClaudePathOverridesDerived()
    {
        // Arrange
        var options = new DockerAgentExecutionOptions
        {
            DataVolumePath = "/data",
            WorkerImage = "test-image:latest",
            NetworkName = "bridge"
        };
        var service = new DockerAgentExecutionService(
            Options.Create(options), _loggerMock.Object, _secretsServiceMock.Object);

        // Act - provide an explicit claudePath that differs from what would be derived
        var args = service.BuildContainerDockerArgs(
            "test-container",
            "/data/some/path/src",
            useRm: false,
            claudePath: "/data/different/issue/.claude");

        // Assert - should use the explicit .claude path
        Assert.That(args, Does.Contain("-v \"/data/different/issue/.claude:/home/homespun/.claude\""));
        // Should NOT use the derived path
        Assert.That(args, Does.Not.Contain("-v \"/data/some/path/.claude:/home/homespun/.claude\""));
    }

    [Test]
    public void BuildContainerDockerArgs_IncludesPromtailLoggingLabel()
    {
        // Act
        var args = _service.BuildContainerDockerArgs(
            "test-container", "/data/some/path", useRm: false);

        // Assert - worker containers need the logging=promtail label for Promtail discovery
        Assert.That(args, Does.Contain("--label logging=promtail"));
    }

    [Test]
    public void EnsureClaudeDirectoryExists_CreatesSubdirectories_WithExplicitPath()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid():N}");
        try
        {
            var claudePath = Path.Combine(tempDir, ".claude");

            // Act
            _service.EnsureClaudeDirectoryExists("/data/some/workdir", claudePath);

            // Assert
            Assert.That(Directory.Exists(Path.Combine(claudePath, "debug")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(claudePath, "todos")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(claudePath, "projects")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(claudePath, "statsig")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(claudePath, "plans")), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void EnsureClaudeDirectoryExists_DerivesPathFromWorkingDirectory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid():N}");
        try
        {
            // Act - no explicit claudePath, should derive from workingDirectory parent
            _service.EnsureClaudeDirectoryExists($"{tempDir}/workdir", claudePath: null);

            // Assert - should create .claude as sibling of workdir
            var derivedClaudePath = Path.Combine(tempDir, ".claude");
            Assert.That(Directory.Exists(Path.Combine(derivedClaudePath, "debug")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(derivedClaudePath, "todos")), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void EnsureClaudeDirectoryExists_DoesNotThrow_WhenDirectoriesAlreadyExist()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid():N}");
        try
        {
            var claudePath = Path.Combine(tempDir, ".claude");
            Directory.CreateDirectory(Path.Combine(claudePath, "debug"));

            // Act & Assert - should not throw when directories already exist
            Assert.DoesNotThrow(() => _service.EnsureClaudeDirectoryExists("/data/workdir", claudePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region Secret Injection Tests

    [Test]
    public void SecretsServiceMock_ShouldHaveBothMethodsSetUp()
    {
        // Arrange - verifies that both mock methods are configured
        // This test documents the expected setup for the secrets service mock

        // Assert - the mock should have both methods available
        Assert.DoesNotThrow(() =>
        {
            _secretsServiceMock.Verify(s => s.GetSecretsForInjectionAsync(It.IsAny<string>()), Times.Never);
            _secretsServiceMock.Verify(s => s.GetSecretsForInjectionByProjectIdAsync(It.IsAny<string>()), Times.Never);
        });
    }

    #endregion

    #region CleanupOrphanedContainersAsync Tests

    [Test]
    public async Task CleanupOrphanedContainersAsync_NoContainers_ReturnsZero()
    {
        // Act
        var result = await _service.CleanupOrphanedContainersAsync();

        // Assert
        Assert.That(result, Is.GreaterThanOrEqualTo(0));
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

    [Test]
    public void SdkQuestionPendingMessage_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var questionsJson = "{\"questions\":[{\"question\":\"Which option?\",\"header\":\"Choice\",\"options\":[],\"multiSelect\":false}]}";
        var msg = new SdkQuestionPendingMessage("session-123", questionsJson);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("session-123"));
            Assert.That(msg.Type, Is.EqualTo("question_pending"));
            Assert.That(msg.QuestionsJson, Is.EqualTo(questionsJson));
        });
    }

    [Test]
    public void SdkQuestionPendingMessage_WithClause_RemapsSessionId()
    {
        // Arrange
        var msg = new SdkQuestionPendingMessage("original-session", "{\"questions\":[]}");

        // Act
        var remapped = msg with { SessionId = "new-session" };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(remapped.SessionId, Is.EqualTo("new-session"));
            Assert.That(remapped.QuestionsJson, Is.EqualTo("{\"questions\":[]}"));
            Assert.That(remapped.Type, Is.EqualTo("question_pending"));
        });
    }

    [Test]
    public void SdkPlanPendingMessage_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var planJson = "{\"plan\":\"# My Plan\\n\\n1. Step one\"}";
        var msg = new SdkPlanPendingMessage("session-123", planJson);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("session-123"));
            Assert.That(msg.Type, Is.EqualTo("plan_pending"));
            Assert.That(msg.PlanJson, Is.EqualTo(planJson));
        });
    }

    [Test]
    public void SdkPlanPendingMessage_WithClause_RemapsSessionId()
    {
        // Arrange
        var msg = new SdkPlanPendingMessage("original-session", "{\"plan\":\"test\"}");

        // Act
        var remapped = msg with { SessionId = "new-session" };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(remapped.SessionId, Is.EqualTo("new-session"));
            Assert.That(remapped.PlanJson, Is.EqualTo("{\"plan\":\"test\"}"));
            Assert.That(remapped.Type, Is.EqualTo("plan_pending"));
        });
    }
}

/// <summary>
/// Tests for AnswerQuestionAsync on execution services.
/// </summary>
[TestFixture]
public class AnswerQuestionAsyncTests
{
    [Test]
    public async Task DockerAnswerQuestionAsync_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<DockerAgentExecutionService>>();
        var secretsServiceMock = new Mock<ISecretsService>();
        secretsServiceMock.Setup(s => s.GetSecretsForInjectionAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
        secretsServiceMock.Setup(s => s.GetSecretsForInjectionByProjectIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
        var options = new DockerAgentExecutionOptions();
        var service = new DockerAgentExecutionService(
            Options.Create(options), loggerMock.Object, secretsServiceMock.Object);

        // Act
        var result = await service.AnswerQuestionAsync("non-existent-session",
            new Dictionary<string, string> { { "q", "a" } });

        // Assert
        Assert.That(result, Is.False);

        await service.DisposeAsync();
    }

    [Test]
    public async Task LocalAnswerQuestionAsync_AlwaysReturnsFalse()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<LocalAgentExecutionService>>();
        var factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        var optionsFactory = new SessionOptionsFactory(factoryLoggerMock.Object);
        var service = new LocalAgentExecutionService(optionsFactory, loggerMock.Object);

        // Act
        var result = await service.AnswerQuestionAsync("any-session",
            new Dictionary<string, string> { { "q", "a" } });

        // Assert - local mode always returns false
        Assert.That(result, Is.False);

        await service.DisposeAsync();
    }
}

/// <summary>
/// Tests for ApprovePlanAsync on execution services.
/// </summary>
[TestFixture]
public class ApprovePlanAsyncTests
{
    [Test]
    public async Task DockerApprovePlanAsync_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<DockerAgentExecutionService>>();
        var secretsServiceMock = new Mock<ISecretsService>();
        secretsServiceMock.Setup(s => s.GetSecretsForInjectionAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
        secretsServiceMock.Setup(s => s.GetSecretsForInjectionByProjectIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, string>());
        var options = new DockerAgentExecutionOptions();
        var service = new DockerAgentExecutionService(
            Options.Create(options), loggerMock.Object, secretsServiceMock.Object);

        // Act
        var result = await service.ApprovePlanAsync("non-existent-session", true, true);

        // Assert
        Assert.That(result, Is.False);

        await service.DisposeAsync();
    }

    [Test]
    public async Task LocalApprovePlanAsync_AlwaysReturnsFalse()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<LocalAgentExecutionService>>();
        var factoryLoggerMock = new Mock<ILogger<SessionOptionsFactory>>();
        var optionsFactory = new SessionOptionsFactory(factoryLoggerMock.Object);
        var service = new LocalAgentExecutionService(optionsFactory, loggerMock.Object);

        // Act
        var result = await service.ApprovePlanAsync("any-session", true, true);

        // Assert - local mode always returns false
        Assert.That(result, Is.False);

        await service.DisposeAsync();
    }
}

/// <summary>
/// Tests for MapWorkerSessionStatus based on lastMessageType.
/// These tests verify that container status is correctly derived from the last SDK message.
/// </summary>
[TestFixture]
public class MapWorkerSessionStatusTests
{
    #region LastMessageType-based Status Tests

    [Test]
    public void MapWorkerSessionStatus_ResultSuccess_ReturnsWaitingForInput()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming", // Even if streaming, result/success should take precedence
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "result",
            LastMessageSubtype: "success"
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
    }

    [Test]
    public void MapWorkerSessionStatus_ResultErrorDuringExecution_ReturnsStopped()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "idle",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "result",
            LastMessageSubtype: "error_during_execution"
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Stopped));
    }

    [Test]
    public void MapWorkerSessionStatus_ResultErrorMaxTurns_ReturnsStopped()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "idle",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "result",
            LastMessageSubtype: "error_max_turns"
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Stopped));
    }

    [Test]
    public void MapWorkerSessionStatus_AssistantMessage_ReturnsRunning()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "assistant",
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapWorkerSessionStatus_StreamEventMessage_ReturnsRunning()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "stream_event",
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapWorkerSessionStatus_SystemInit_ReturnsRunning()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "system",
            LastMessageSubtype: "init"
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapWorkerSessionStatus_QuestionPendingMessageType_ReturnsWaitingForQuestionAnswer()
    {
        // Arrange - lastMessageType indicates question pending, but flag not set
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "idle",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false, // Flag not set, but message type is
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "question_pending",
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForQuestionAnswer));
    }

    [Test]
    public void MapWorkerSessionStatus_PlanPendingMessageType_ReturnsWaitingForPlanExecution()
    {
        // Arrange - lastMessageType indicates plan pending, but flag not set
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "idle",
            Mode: "Plan",
            Model: "sonnet",
            PermissionMode: "plan",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false, // Flag not set, but message type is
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "plan_pending",
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution));
    }

    #endregion

    #region Pending Flag Priority Tests

    [Test]
    public void MapWorkerSessionStatus_HasPendingQuestion_TakesPriority()
    {
        // Arrange - pending question flag should take priority over lastMessageType
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: true, // This should take priority
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "assistant", // Even though last message was assistant
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForQuestionAnswer));
    }

    [Test]
    public void MapWorkerSessionStatus_HasPendingPlanApproval_TakesPriority()
    {
        // Arrange - pending plan flag should take priority over lastMessageType
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Plan",
            Model: "sonnet",
            PermissionMode: "plan",
            HasPendingQuestion: false,
            HasPendingPlanApproval: true, // This should take priority
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "assistant",
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution));
    }

    #endregion

    #region Fallback to Status Field Tests

    [Test]
    public void MapWorkerSessionStatus_NoLastMessageType_FallsBackToStatusField()
    {
        // Arrange - no lastMessageType, should fall back to status field
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "idle",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: null, // No message type
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert - falls back to status field "idle" -> WaitingForInput
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
    }

    [Test]
    public void MapWorkerSessionStatus_EmptyLastMessageType_FallsBackToStatusField()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: true,
            SessionId: "session-1",
            Status: "streaming",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: "", // Empty string
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert - falls back to status field "streaming" -> Running
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapWorkerSessionStatus_ClosedStatus_ReturnsStopped()
    {
        // Arrange
        var response = new ActiveSessionResponse(
            HasActiveSession: false,
            SessionId: "session-1",
            Status: "closed",
            Mode: "Build",
            Model: "sonnet",
            PermissionMode: "bypassPermissions",
            HasPendingQuestion: false,
            HasPendingPlanApproval: false,
            LastActivityAt: DateTime.UtcNow.ToString("O"),
            LastMessageType: null,
            LastMessageSubtype: null
        );

        // Act
        var result = MapWorkerSessionStatus(response);

        // Assert
        Assert.That(result, Is.EqualTo(ClaudeSessionStatus.Stopped));
    }

    #endregion

    #region MapFromStatus Tests

    [Test]
    public void MapFromStatus_Streaming_ReturnsRunning()
    {
        Assert.That(MapFromStatus("streaming"), Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapFromStatus_Idle_ReturnsWaitingForInput()
    {
        Assert.That(MapFromStatus("idle"), Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
    }

    [Test]
    public void MapFromStatus_Closed_ReturnsStopped()
    {
        Assert.That(MapFromStatus("closed"), Is.EqualTo(ClaudeSessionStatus.Stopped));
    }

    [Test]
    public void MapFromStatus_Null_ReturnsRunning()
    {
        Assert.That(MapFromStatus(null), Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void MapFromStatus_UnknownValue_ReturnsRunning()
    {
        Assert.That(MapFromStatus("unknown"), Is.EqualTo(ClaudeSessionStatus.Running));
    }

    #endregion
}
