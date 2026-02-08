using Bunit;
using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Components.SessionInfoPanel;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Projects;
using Homespun.Features.PullRequests.Data.Entities;
using Homespun.Features.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionInfoPanelTests : BunitTestContext
{
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitCloneService> _mockGitService = null!;
    private Mock<ITodoParser> _mockTodoParser = null!;
    private Mock<IIssuePrStatusService> _mockPrStatusService = null!;
    private Mock<IMarkdownRenderingService> _mockMarkdownService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();

        _mockFleeceService = new Mock<IFleeceService>();
        _mockProjectService = new Mock<IProjectService>();
        _mockGitService = new Mock<IGitCloneService>();
        _mockTodoParser = new Mock<ITodoParser>();
        _mockPrStatusService = new Mock<IIssuePrStatusService>();
        _mockMarkdownService = new Mock<IMarkdownRenderingService>();

        Services.AddSingleton(_mockFleeceService.Object);
        Services.AddSingleton(_mockProjectService.Object);
        Services.AddSingleton(_mockGitService.Object);
        Services.AddSingleton(_mockTodoParser.Object);
        Services.AddSingleton(_mockPrStatusService.Object);
        Services.AddSingleton(_mockMarkdownService.Object);

        // Default setups
        _mockProjectService.Setup(p => p.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Project { Id = "proj-1", LocalPath = "/test/path", Name = "Test", DefaultBranch = "main" });

        _mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string?>()))
            .Returns((string? text) => text ?? "");

        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(new List<SessionTodoItem>());

        _mockGitService.Setup(g => g.GetChangedFilesAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<FileChangeInfo>());

        _mockFleeceService.Setup(s => s.GetIssueAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new Issue { Id = "ABC123", Title = "Test Issue", Status = IssueStatus.Progress, Type = IssueType.Feature, LastUpdate = DateTime.UtcNow });

        _mockPrStatusService.Setup(s => s.GetPullRequestStatusForIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IssuePullRequestStatus?)null);
    }

    private static ClaudeSession CreateTestSession()
    {
        return new ClaudeSession
        {
            Id = "session-1",
            EntityId = "ABC123",
            ProjectId = "proj-1",
            WorkingDirectory = "/test/clone",
            Mode = SessionMode.Build,
            Model = "sonnet",
            Status = ClaudeSessionStatus.Running
        };
    }

    [Test]
    public void SessionInfoPanel_Renders_AllFourTabs()
    {
        // Arrange
        var session = CreateTestSession();

        // Act
        var cut = Render<SessionInfoPanel>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.IsOpen, true));

        // Assert - scope to desktop panel since mobile panel also renders tabs
        var desktopPanel = cut.Find(".desktop-panel");
        var tabButtons = desktopPanel.QuerySelectorAll(".tab-button");
        Assert.That(tabButtons, Has.Count.EqualTo(4));
    }

    [Test]
    public void SessionInfoPanel_DefaultsTo_IssueTab()
    {
        // Arrange
        var session = CreateTestSession();

        // Act
        var cut = Render<SessionInfoPanel>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.IsOpen, true));

        // Assert - scope to desktop panel since mobile panel also renders tabs
        var desktopPanel = cut.Find(".desktop-panel");
        var activeTab = desktopPanel.QuerySelector(".tab-button.active");
        Assert.That(activeTab, Is.Not.Null);
        Assert.That(activeTab!.TextContent.Trim(), Does.Contain("Issue"));
    }

    [Test]
    public void SessionInfoPanel_WhenClosed_HidesContent()
    {
        // Arrange
        var session = CreateTestSession();

        // Act
        var cut = Render<SessionInfoPanel>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.IsOpen, false));

        // Assert
        var panel = cut.Find(".desktop-panel");
        Assert.That(panel.ClassList, Does.Contain("collapsed"));
    }

    [Test]
    public void SessionInfoPanel_WhenOpen_ShowsContent()
    {
        // Arrange
        var session = CreateTestSession();

        // Act
        var cut = Render<SessionInfoPanel>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.IsOpen, true));

        // Assert
        var panel = cut.Find(".desktop-panel");
        Assert.That(panel.ClassList, Does.Not.Contain("collapsed"));
    }

    [Test]
    public void SessionInfoPanel_NullSession_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionInfoPanel>(parameters => parameters
            .Add(p => p.IsOpen, true));

        // Assert
        var emptyState = cut.Find(".panel-empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No session"));
    }
}
