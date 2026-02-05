using Bunit;
using Homespun.Features.ClaudeCode.Components.SessionInfoPanel;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.Git;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionFilesTabTests : BunitTestContext
{
    private Mock<IGitWorktreeService> _mockGitService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockGitService = new Mock<IGitWorktreeService>();
        Services.AddSingleton(_mockGitService.Object);
    }

    [Test]
    public void SessionFilesTab_WithFiles_DisplaysList()
    {
        // Arrange
        var files = new List<FileChangeInfo>
        {
            new() { FilePath = "src/Component.cs", Additions = 10, Deletions = 5, Status = FileChangeStatus.Modified },
            new() { FilePath = "src/NewFile.cs", Additions = 20, Deletions = 0, Status = FileChangeStatus.Added }
        };
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(files);

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        // Wait for async load
        cut.WaitForState(() => cut.FindAll(".file-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var fileItems = cut.FindAll(".file-item");
        Assert.That(fileItems, Has.Count.EqualTo(2));
    }

    [Test]
    public void SessionFilesTab_AddedFile_ShowsGreenPlus()
    {
        // Arrange
        var files = new List<FileChangeInfo>
        {
            new() { FilePath = "src/NewFile.cs", Additions = 20, Deletions = 0, Status = FileChangeStatus.Added }
        };
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(files);

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        cut.WaitForState(() => cut.FindAll(".file-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var statusIcon = cut.Find(".status-icon.added");
        Assert.That(statusIcon.TextContent, Does.Contain("+"));
    }

    [Test]
    public void SessionFilesTab_ModifiedFile_ShowsYellowDot()
    {
        // Arrange
        var files = new List<FileChangeInfo>
        {
            new() { FilePath = "src/Modified.cs", Additions = 5, Deletions = 3, Status = FileChangeStatus.Modified }
        };
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(files);

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        cut.WaitForState(() => cut.FindAll(".file-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var statusIcon = cut.Find(".status-icon.modified");
        Assert.That(statusIcon, Is.Not.Null);
    }

    [Test]
    public void SessionFilesTab_DeletedFile_ShowsRedMinus()
    {
        // Arrange
        var files = new List<FileChangeInfo>
        {
            new() { FilePath = "src/Deleted.cs", Additions = 0, Deletions = 30, Status = FileChangeStatus.Deleted }
        };
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(files);

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        cut.WaitForState(() => cut.FindAll(".file-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var statusIcon = cut.Find(".status-icon.deleted");
        Assert.That(statusIcon.TextContent, Does.Contain("-"));
    }

    [Test]
    public void SessionFilesTab_NoFiles_ShowsEmptyState()
    {
        // Arrange
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<FileChangeInfo>());

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        cut.WaitForState(() => cut.Find(".empty-state") != null, TimeSpan.FromSeconds(1));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No file changes"));
    }

    [Test]
    public void SessionFilesTab_Loading_ShowsSpinner()
    {
        // Arrange - Don't complete the task immediately
        var tcs = new TaskCompletionSource<List<FileChangeInfo>>();
        _mockGitService.Setup(s => s.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(tcs.Task);

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        // Assert - Should show loading state
        var loading = cut.Find(".loading-state");
        Assert.That(loading, Is.Not.Null);

        // Clean up
        tcs.SetResult([]);
    }

    [Test]
    public void SessionFilesTab_NoWorkingDirectory_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.TargetBranch, "main"));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState, Is.Not.Null);
    }
}
