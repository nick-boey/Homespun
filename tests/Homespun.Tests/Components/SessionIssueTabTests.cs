using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Features.Chat.Components.SessionInfoPanel;
using Homespun.Client.Services;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionIssueTabTests : BunitTestContext
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
        Services.AddSingleton(new HttpIssueApiService(httpClient));
        Services.AddSingleton(_mockMarkdownService.Object);

        // Setup markdown service
        _mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string?>()))
            .Returns((string? text) => text ?? "");
    }

    private static Issue CreateTestIssue(string id, string title, IssueStatus status = IssueStatus.Progress, IssueType type = IssueType.Feature, string? description = null, int? priority = null)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = status,
            Type = type,
            Description = description,
            Priority = priority,
            LastUpdate = DateTime.UtcNow
        };
    }

    [Test]
    public void SessionIssueTab_WithIssue_DisplaysTitle()
    {
        // Arrange
        var issue = CreateTestIssue("ABC123", "Test Issue Title");
        _mockHandler.RespondWith("api/issues/ABC123", issue);

        // Act
        var cut = Render<SessionIssueTab>(parameters => parameters
            .Add(p => p.EntityId, "ABC123")
            .Add(p => p.ProjectId, "proj-1"));

        cut.WaitForState(() => cut.Find(".issue-title") != null, TimeSpan.FromSeconds(1));

        // Assert
        var title = cut.Find(".issue-title");
        Assert.That(title.TextContent, Does.Contain("Test Issue Title"));
    }

    [Test]
    public void SessionIssueTab_NoIssue_ShowsEmptyState()
    {
        // Arrange
        _mockHandler.RespondNotFound("api/issues/");

        // Act
        var cut = Render<SessionIssueTab>(parameters => parameters
            .Add(p => p.EntityId, "nonexistent")
            .Add(p => p.ProjectId, "proj-1"));

        cut.WaitForState(() => cut.Find(".empty-state") != null, TimeSpan.FromSeconds(1));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("Issue not found"));
    }

    [Test]
    public void SessionIssueTab_CloneEntity_ShowsNotLinkedMessage()
    {
        // Act - clone entities start with "clone:"
        var cut = Render<SessionIssueTab>(parameters => parameters
            .Add(p => p.EntityId, "clone:feature/test")
            .Add(p => p.ProjectId, "proj-1"));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No issue linked"));
    }

    [Test]
    public void SessionIssueTab_NoProjectId_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionIssueTab>(parameters => parameters
            .Add(p => p.EntityId, "ABC123"));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState, Is.Not.Null);
    }
}
