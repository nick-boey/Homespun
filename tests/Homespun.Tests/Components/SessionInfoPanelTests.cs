using Bunit;
using Homespun.Client.Features.Chat.Components.SessionInfoPanel;
using Homespun.Client.Services;
using Homespun.Shared.Models.Plans;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionInfoPanelTests : BunitTestContext
{
    [SetUp]
    public new void Setup()
    {
        base.Setup();

        var mockHandler = new MockHttpMessageHandler();

        // Default responses for child tab HTTP calls
        mockHandler
            .RespondWith("changed-files", new List<FileChangeInfo>())
            .RespondWith("api/plans", new List<PlanFileInfo>())
            .RespondNotFound("api/issues/")
            .RespondNotFound("api/issue-pr-status/");

        var httpClient = mockHandler.CreateClient();

        // Register HTTP API services needed by child tabs
        Services.AddSingleton(new HttpCloneApiService(httpClient));
        Services.AddSingleton(new HttpIssueApiService(httpClient));
        Services.AddSingleton(new HttpIssuePrStatusApiService(httpClient));
        Services.AddSingleton(new HttpPlansApiService(httpClient));

        // Register IMarkdownRenderingService needed by SessionIssueTab and SessionPrTab
        var mockMarkdownService = new Mock<IMarkdownRenderingService>();
        mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string?>()))
            .Returns((string? text) => text ?? "");
        Services.AddSingleton(mockMarkdownService.Object);
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
    public void SessionInfoPanel_Renders_AllFiveTabs()
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
        Assert.That(tabButtons, Has.Count.EqualTo(5));
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
