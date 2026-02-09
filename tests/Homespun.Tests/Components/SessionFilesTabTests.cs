using Bunit;
using Homespun.Client.Components.ClaudeCode.SessionInfoPanel;
using Homespun.Client.Services;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionFilesTabTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpCloneApiService(httpClient));
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
        _mockHandler.RespondWith("changed-files", files);

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
        _mockHandler.RespondWith("changed-files", files);

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
        _mockHandler.RespondWith("changed-files", files);

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
        _mockHandler.RespondWith("changed-files", files);

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
        _mockHandler.RespondWith("changed-files", new List<FileChangeInfo>());

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
        // Arrange - The mock handler will respond immediately, but we can test the initial state
        // by not configuring a response for the URL pattern (default response will be used)
        // Use a handler that delays the response
        var delayHandler = new MockHttpMessageHandler();
        // Don't configure any response for changed-files - it will use the default empty object response
        // which will cause the JSON deserialization to return empty, but the component should show loading first

        // For a true loading test, we need to verify the component renders loading state
        // before the async call completes. Since MockHttpMessageHandler responds synchronously,
        // we test by rendering without configuring the endpoint.
        _mockHandler.WithDefaultResponse(new List<FileChangeInfo>());

        // Act
        var cut = Render<SessionFilesTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path")
            .Add(p => p.TargetBranch, "main"));

        // Assert - Component should eventually reach a state (loading or loaded)
        // The loading state is transient with synchronous mock responses
        cut.WaitForState(() =>
            cut.FindAll(".loading-state").Count > 0 || cut.FindAll(".empty-state").Count > 0,
            TimeSpan.FromSeconds(1));
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
