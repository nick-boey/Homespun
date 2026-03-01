using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for ContainerDiscoveryService.
/// Tests the parsing of Docker container metadata for server restart recovery.
/// </summary>
[TestFixture]
public class ContainerDiscoveryServiceTests
{
    private Mock<ILogger<ContainerDiscoveryService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<ContainerDiscoveryService>>();
    }

    #region ParseDockerPsJson Tests

    [Test]
    public void ParseDockerPsJson_ValidJson_ReturnsContainerInfo()
    {
        // Arrange
        var json = """
        {
          "ID": "abc123def456",
          "Names": "homespun-issue-proj1-issue1",
          "Labels": "homespun.managed=true,homespun.type=worker,homespun.working.directory=/data/projects/test,homespun.created.at=2026-03-01T10:00:00Z,homespun.project.id=proj1,homespun.issue.id=issue1,logging=promtail"
        }
        """;

        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.containerId, Is.EqualTo("abc123def456"));
            Assert.That(result.Value.containerName, Is.EqualTo("homespun-issue-proj1-issue1"));
            Assert.That(result.Value.workingDirectory, Is.EqualTo("/data/projects/test"));
            Assert.That(result.Value.projectId, Is.EqualTo("proj1"));
            Assert.That(result.Value.issueId, Is.EqualTo("issue1"));
            Assert.That(result.Value.createdAt.Year, Is.EqualTo(2026));
        });
    }

    [Test]
    public void ParseDockerPsJson_MissingRequiredLabel_ReturnsNull()
    {
        // Arrange - missing homespun.working.directory
        var json = """
        {
          "ID": "abc123def456",
          "Names": "homespun-issue-proj1-issue1",
          "Labels": "homespun.managed=true,homespun.type=worker,logging=promtail"
        }
        """;

        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson(json);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseDockerPsJson_NotHomespunManaged_ReturnsNull()
    {
        // Arrange - missing homespun.managed=true
        var json = """
        {
          "ID": "abc123def456",
          "Names": "some-other-container",
          "Labels": "logging=promtail"
        }
        """;

        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson(json);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseDockerPsJson_WithoutProjectAndIssue_ReturnsContainerWithNullIds()
    {
        // Arrange - container without project/issue labels (e.g., per-session container)
        var json = """
        {
          "ID": "abc123def456",
          "Names": "homespun-agent-12345678",
          "Labels": "homespun.managed=true,homespun.type=worker,homespun.working.directory=/data/workdir,homespun.created.at=2026-03-01T10:00:00Z"
        }
        """;

        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.projectId, Is.Null);
            Assert.That(result.Value.issueId, Is.Null);
            Assert.That(result.Value.workingDirectory, Is.EqualTo("/data/workdir"));
        });
    }

    [Test]
    public void ParseDockerPsJson_InvalidJson_ReturnsNull()
    {
        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson("not valid json");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseDockerPsJson_EmptyJson_ReturnsNull()
    {
        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson("");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseDockerPsJson_InvalidDateFormat_UsesMinValue()
    {
        // Arrange - invalid date format
        var json = """
        {
          "ID": "abc123def456",
          "Names": "homespun-issue-proj1-issue1",
          "Labels": "homespun.managed=true,homespun.type=worker,homespun.working.directory=/data/projects/test,homespun.created.at=not-a-date"
        }
        """;

        // Act
        var result = ContainerDiscoveryService.ParseDockerPsJson(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.createdAt, Is.EqualTo(DateTime.MinValue));
    }

    #endregion

    #region ParseLabelValue Tests

    [Test]
    public void ParseLabelValue_ExistingLabel_ReturnsValue()
    {
        // Arrange
        var labels = "homespun.managed=true,homespun.type=worker,homespun.working.directory=/data/test";

        // Act
        var result = ContainerDiscoveryService.ParseLabelValue(labels, "homespun.working.directory");

        // Assert
        Assert.That(result, Is.EqualTo("/data/test"));
    }

    [Test]
    public void ParseLabelValue_NonExistingLabel_ReturnsNull()
    {
        // Arrange
        var labels = "homespun.managed=true,homespun.type=worker";

        // Act
        var result = ContainerDiscoveryService.ParseLabelValue(labels, "homespun.project.id");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseLabelValue_EmptyLabels_ReturnsNull()
    {
        // Act
        var result = ContainerDiscoveryService.ParseLabelValue("", "homespun.managed");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseLabelValue_LabelWithEqualsInValue_ReturnsFullValue()
    {
        // Arrange - edge case where value contains equals sign
        var labels = "homespun.managed=true,homespun.custom=a=b=c";

        // Act
        var result = ContainerDiscoveryService.ParseLabelValue(labels, "homespun.custom");

        // Assert
        Assert.That(result, Is.EqualTo("a=b=c"));
    }

    #endregion

    #region DiscoveredContainer Record Tests

    [Test]
    public void DiscoveredContainer_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var container = new DiscoveredContainer(
            ContainerId: "abc123",
            ContainerName: "homespun-test",
            WorkerUrl: "http://172.17.0.5:8080",
            ProjectId: "proj-1",
            IssueId: "issue-1",
            WorkingDirectory: "/data/test",
            CreatedAt: new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(container.ContainerId, Is.EqualTo("abc123"));
            Assert.That(container.ContainerName, Is.EqualTo("homespun-test"));
            Assert.That(container.WorkerUrl, Is.EqualTo("http://172.17.0.5:8080"));
            Assert.That(container.ProjectId, Is.EqualTo("proj-1"));
            Assert.That(container.IssueId, Is.EqualTo("issue-1"));
            Assert.That(container.WorkingDirectory, Is.EqualTo("/data/test"));
            Assert.That(container.CreatedAt.Year, Is.EqualTo(2026));
        });
    }

    [Test]
    public void DiscoveredContainer_AllowsNullProjectAndIssue()
    {
        // Arrange & Act
        var container = new DiscoveredContainer(
            ContainerId: "abc123",
            ContainerName: "homespun-agent-xyz",
            WorkerUrl: "http://172.17.0.5:8080",
            ProjectId: null,
            IssueId: null,
            WorkingDirectory: "/data/test",
            CreatedAt: DateTime.UtcNow);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(container.ProjectId, Is.Null);
            Assert.That(container.IssueId, Is.Null);
        });
    }

    #endregion
}
