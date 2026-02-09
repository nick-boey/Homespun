using Bunit;
using Homespun.Features.ClaudeCode.Components.SessionInfoPanel;
using Homespun.Features.Projects;
using Homespun.Features.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionPrTabTests : BunitTestContext
{
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IIssuePrStatusService> _mockPrStatusService = null!;
    private Mock<IMarkdownRenderingService> _mockMarkdownService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();

        _mockProjectService = new Mock<IProjectService>();
        _mockPrStatusService = new Mock<IIssuePrStatusService>();
        _mockMarkdownService = new Mock<IMarkdownRenderingService>();

        Services.AddSingleton(_mockProjectService.Object);
        Services.AddSingleton(_mockPrStatusService.Object);
        Services.AddSingleton(_mockMarkdownService.Object);

        // Setup project service
        _mockProjectService.Setup(p => p.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new Project { Id = "proj-1", LocalPath = "/test/path", Name = "Test", GitHubOwner = "owner", GitHubRepo = "repo", DefaultBranch = "main" });

        // Setup markdown service
        _mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string?>()))
            .Returns((string? text) => text ?? "");

        // Setup PR status service
        _mockPrStatusService.Setup(s => s.GetPullRequestStatusForIssueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IssuePullRequestStatus?)null);
    }

    [Test]
    public void SessionPrTab_WithPr_DisplaysPrNumber()
    {
        // Arrange
        var prStatus = new IssuePullRequestStatus
        {
            PrNumber = 123,
            PrUrl = "https://github.com/owner/repo/pull/123",
            Status = PullRequestStatus.InProgress
        };
        _mockPrStatusService.Setup(s => s.GetPullRequestStatusForIssueAsync("proj-1", "ABC123"))
            .ReturnsAsync(prStatus);

        // Act
        var cut = Render<SessionPrTab>(parameters => parameters
            .Add(p => p.EntityId, "ABC123")
            .Add(p => p.ProjectId, "proj-1"));

        cut.WaitForState(() => cut.FindAll(".pr-number").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var prNumber = cut.Find(".pr-number");
        Assert.That(prNumber.TextContent, Does.Contain("#123"));
    }

    [Test]
    public void SessionPrTab_NoPr_ShowsEmptyState()
    {
        // Arrange
        _mockPrStatusService.Setup(s => s.GetPullRequestStatusForIssueAsync("proj-1", "ABC123"))
            .ReturnsAsync((IssuePullRequestStatus?)null);

        // Act
        var cut = Render<SessionPrTab>(parameters => parameters
            .Add(p => p.EntityId, "ABC123")
            .Add(p => p.ProjectId, "proj-1"));

        cut.WaitForState(() => cut.Find(".empty-state") != null, TimeSpan.FromSeconds(1));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No pull request"));
    }

    [Test]
    public void SessionPrTab_CloneEntity_ShowsNoPrMessage()
    {
        // Act
        var cut = Render<SessionPrTab>(parameters => parameters
            .Add(p => p.EntityId, "clone:feature/test")
            .Add(p => p.ProjectId, "proj-1"));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No pull request"));
    }

    [Test]
    public void SessionPrTab_NoProjectId_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionPrTab>(parameters => parameters
            .Add(p => p.EntityId, "ABC123"));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState, Is.Not.Null);
    }
}
