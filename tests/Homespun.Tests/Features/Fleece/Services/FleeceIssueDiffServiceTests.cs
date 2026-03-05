using NUnit.Framework;
using Moq;
using Homespun.Server.Features.Fleece.Services;
using Homespun.Server.Features.Commands.Services;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Logging;
using Fleece.Core.Models;
using System.Text.Json;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class FleeceIssueDiffServiceTests
{
    private Mock<ICommandExecutor> _commandExecutorMock = null!;
    private Mock<ILogger<FleeceIssueDiffService>> _loggerMock = null!;
    private FleeceIssueDiffService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _commandExecutorMock = new Mock<ICommandExecutor>();
        _loggerMock = new Mock<ILogger<FleeceIssueDiffService>>();
        _service = new FleeceIssueDiffService(_commandExecutorMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithNoChanges_ReturnsEmptyList()
    {
        // Arrange
        var workingDirectory = "/test/repo";

        // Mock git diff to show no changes
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff")),
            It.Is<string>(dir => dir == workingDirectory),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "",
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithNewIssue_ReturnsAddedDiff()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var newIssue = new Issue("test123", "New Feature")
        {
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            Description = "Test description"
        };
        var issueJson = JsonSerializer.Serialize(newIssue);

        // Mock git diff showing new file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff --name-status")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "A\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading new file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("cat") && cmd.Contains("test123.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = issueJson,
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Created));
        Assert.That(diff.OriginalIssue, Is.Null);
        Assert.That(diff.ModifiedIssue, Is.Not.Null);
        Assert.That(diff.ModifiedIssue!.Title, Is.EqualTo("New Feature"));
        Assert.That(diff.ChangedFields, Is.Empty);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithModifiedIssue_ReturnsModifiedDiffWithChangedFields()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var originalIssue = new Issue("test123", "Original Title")
        {
            Status = IssueStatus.Open,
            Type = IssueType.Feature,
            Description = "Original description"
        };
        var modifiedIssue = new Issue("test123", "Modified Title")
        {
            Status = IssueStatus.Progress,
            Type = IssueType.Feature,
            Description = "Modified description"
        };

        // Mock git diff showing modified file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff --name-status")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "M\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading original file from HEAD
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git show HEAD:.fleece/issues/test123.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(originalIssue),
                Error = ""
            });

        // Mock reading modified file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("cat") && cmd.Contains("test123.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(modifiedIssue),
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Updated));
        Assert.That(diff.OriginalIssue, Is.Not.Null);
        Assert.That(diff.OriginalIssue!.Title, Is.EqualTo("Original Title"));
        Assert.That(diff.ModifiedIssue, Is.Not.Null);
        Assert.That(diff.ModifiedIssue!.Title, Is.EqualTo("Modified Title"));
        Assert.That(diff.ChangedFields, Contains.Item("Title"));
        Assert.That(diff.ChangedFields, Contains.Item("Status"));
        Assert.That(diff.ChangedFields, Contains.Item("Description"));
        Assert.That(diff.ChangedFields.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithDeletedIssue_ReturnsDeletedDiff()
    {
        // Arrange
        var workingDirectory = "/test/repo";
        var deletedIssue = new Issue("test123", "Deleted Issue")
        {
            Status = IssueStatus.Open,
            Type = IssueType.Bug
        };

        // Mock git diff showing deleted file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff --name-status")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "D\t.fleece/issues/test123.json",
                Error = ""
            });

        // Mock reading original file from HEAD
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git show HEAD:.fleece/issues/test123.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(deletedIssue),
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var diff = result[0];
        Assert.That(diff.IssueId, Is.EqualTo("test123"));
        Assert.That(diff.ChangeType, Is.EqualTo(IssueChangeType.Deleted));
        Assert.That(diff.OriginalIssue, Is.Not.Null);
        Assert.That(diff.OriginalIssue!.Title, Is.EqualTo("Deleted Issue"));
        Assert.That(diff.ModifiedIssue, Is.Null);
        Assert.That(diff.ChangedFields, Is.Empty);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithMultipleChanges_ReturnsAllDiffs()
    {
        // Arrange
        var workingDirectory = "/test/repo";

        // Mock git diff showing multiple changes
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff --name-status")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "A\t.fleece/issues/new001.json\nM\t.fleece/issues/mod001.json\nD\t.fleece/issues/del001.json",
                Error = ""
            });

        // Setup mocks for each issue type
        var newIssue = new Issue("new001", "New Issue") { Status = IssueStatus.Open };
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("cat") && cmd.Contains("new001.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(newIssue),
                Error = ""
            });

        var originalModIssue = new Issue("mod001", "Original") { Status = IssueStatus.Open };
        var modifiedModIssue = new Issue("mod001", "Modified") { Status = IssueStatus.Progress };
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git show HEAD:.fleece/issues/mod001.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(originalModIssue),
                Error = ""
            });
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("cat") && cmd.Contains("mod001.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(modifiedModIssue),
                Error = ""
            });

        var deletedIssue = new Issue("del001", "Deleted") { Status = IssueStatus.Complete };
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git show HEAD:.fleece/issues/del001.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = JsonSerializer.Serialize(deletedIssue),
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Any(d => d.IssueId == "new001" && d.ChangeType == IssueChangeType.Created), Is.True);
        Assert.That(result.Any(d => d.IssueId == "mod001" && d.ChangeType == IssueChangeType.Updated), Is.True);
        Assert.That(result.Any(d => d.IssueId == "del001" && d.ChangeType == IssueChangeType.Deleted), Is.True);
    }

    [Test]
    public async Task GetIssueDiffsAsync_WithInvalidJson_SkipsInvalidFiles()
    {
        // Arrange
        var workingDirectory = "/test/repo";

        // Mock git diff showing new file
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("git diff --name-status")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "A\t.fleece/issues/invalid.json",
                Error = ""
            });

        // Mock reading invalid JSON
        _commandExecutorMock.Setup(x => x.ExecuteCommandAsync(
            It.Is<string>(cmd => cmd.Contains("cat") && cmd.Contains("invalid.json")),
            workingDirectory,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult
            {
                Success = true,
                Output = "{ invalid json",
                Error = ""
            });

        // Act
        var result = await _service.GetIssueDiffsAsync(workingDirectory);

        // Assert
        Assert.That(result, Is.Empty);
        _loggerMock.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse issue")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}