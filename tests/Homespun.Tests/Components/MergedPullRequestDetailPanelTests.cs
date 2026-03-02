using Bunit;
using Fleece.Core.Models;
using Homespun.Client.Components;
using Homespun.Client.Features.Issues.Components;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.PullRequests;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the MergedPullRequestDetailPanel component.
/// </summary>
[TestFixture]
public class MergedPullRequestDetailPanelTests : BunitTestContext
{
    private Mock<IMarkdownRenderingService> _mockMarkdownService = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockMarkdownService = new Mock<IMarkdownRenderingService>();
        _mockMarkdownService.Setup(m => m.RenderToHtml(It.IsAny<string>()))
            .Returns<string>(s => $"<p>{s}</p>");
        Context!.Services.AddSingleton(_mockMarkdownService.Object);
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersTitle()
    {
        // Arrange
        var details = CreateDetails(title: "Test PR Title");

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Test PR Title"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersMergedBadge()
    {
        // Arrange
        var details = CreateDetails();

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Merged"));
        Assert.That(cut.Markup, Does.Contain("bg-purple"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersPrNumber()
    {
        // Arrange
        var details = CreateDetails(prNumber: 42);

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("PR #42"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersPrLink()
    {
        // Arrange
        var details = CreateDetails(prNumber: 99, htmlUrl: "https://github.com/owner/repo/pull/99");

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        var link = cut.Find("a[target='_blank']");
        Assert.That(link.GetAttribute("href"), Is.EqualTo("https://github.com/owner/repo/pull/99"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersDescription()
    {
        // Arrange
        var details = CreateDetails(body: "This is the PR description");

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("This is the PR description"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersBranchName()
    {
        // Arrange
        var details = CreateDetails(branchName: "feature/test-branch+abc123");

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("feature/test-branch+abc123"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_WithLinkedIssue_RendersIssueDetails()
    {
        // Arrange
        var linkedIssue = CreateLinkedIssue(id: "abc123", title: "Linked Issue Title");
        var details = CreateDetails(linkedIssueId: "abc123", linkedIssue: linkedIssue);

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("abc123"));
        Assert.That(cut.Markup, Does.Contain("Linked Issue Title"));
        Assert.That(cut.Markup, Does.Contain("Linked Issue"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_WithLinkedIssue_RendersEditButton()
    {
        // Arrange
        var linkedIssue = CreateLinkedIssue(id: "abc123");
        var details = CreateDetails(linkedIssueId: "abc123", linkedIssue: linkedIssue);

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert - BbButton with Href renders as an anchor element
        var editLink = cut.Find("a[href='/projects/project-1/issues/abc123/edit']");
        Assert.That(editLink.TextContent, Does.Contain("Edit"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_WithLinkedIssueNotFound_RendersNotFoundMessage()
    {
        // Arrange
        var details = CreateDetails(linkedIssueId: "abc123", linkedIssue: null);

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("abc123"));
        Assert.That(cut.Markup, Does.Contain("not found"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_WithoutLinkedIssue_DoesNotRenderIssueSection()
    {
        // Arrange
        var details = CreateDetails(linkedIssueId: null, linkedIssue: null);

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("Linked Issue"));
    }

    [Test]
    public void MergedPullRequestDetailPanel_CloseButton_InvokesOnClose()
    {
        // Arrange
        var details = CreateDetails();
        var closeCalled = false;

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1")
                .Add(p => p.OnClose, () => { closeCalled = true; }));

        cut.Find(".btn-close").Click();

        // Assert
        Assert.That(closeCalled, Is.True);
    }

    [Test]
    public void MergedPullRequestDetailPanel_RendersHeader()
    {
        // Arrange
        var details = CreateDetails();

        // Act
        var cut = Render<MergedPullRequestDetailPanel>(parameters =>
            parameters
                .Add(p => p.Details, details)
                .Add(p => p.ProjectId, "project-1"));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Merged Pull Request"));
    }

    #region Helper Methods

    private static MergedPullRequestDetails CreateDetails(
        int prNumber = 1,
        string title = "Test PR",
        string? body = null,
        string branchName = "feature/test",
        string? htmlUrl = "https://github.com/test/repo/pull/1",
        string? linkedIssueId = null,
        IssueResponse? linkedIssue = null)
    {
        return new MergedPullRequestDetails
        {
            PullRequest = new PullRequestInfo
            {
                Number = prNumber,
                Title = title,
                Body = body,
                Status = PullRequestStatus.Merged,
                BranchName = branchName,
                HtmlUrl = htmlUrl,
                CreatedAt = DateTime.UtcNow.AddDays(-7),
                MergedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            LinkedIssueId = linkedIssueId,
            LinkedIssue = linkedIssue
        };
    }

    private static IssueResponse CreateLinkedIssue(
        string id = "test123",
        string title = "Test Issue",
        IssueType type = IssueType.Task,
        IssueStatus status = IssueStatus.Complete)
    {
        return new IssueResponse
        {
            Id = id,
            Title = title,
            Type = type,
            Status = status,
            Description = "Test issue description",
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7)
        };
    }

    #endregion
}
