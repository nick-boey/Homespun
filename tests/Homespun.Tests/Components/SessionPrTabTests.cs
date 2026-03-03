using Bunit;
using Homespun.Client.Features.Chat.Components.SessionInfoPanel;
using Homespun.Client.Services;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionPrTabTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;
    private Mock<IMarkdownRenderingService> _mockMarkdownService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();

        _mockHandler = new MockHttpMessageHandler();
        _mockMarkdownService = new Mock<IMarkdownRenderingService>();

        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpIssuePrStatusApiService(httpClient));
        Services.AddSingleton(_mockMarkdownService.Object);

        // Setup markdown service
        _mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string?>()))
            .Returns((string? text) => text ?? "");

        // Default: no PR status found
        _mockHandler.RespondNotFound("api/issue-pr-status/");
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
        _mockHandler.RespondWith("api/issue-pr-status/proj-1/ABC123", prStatus);

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
        _mockHandler.RespondNotFound("api/issue-pr-status/proj-1/ABC123");

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
