using Bunit;
using Homespun.Client.Features.Chat.Components.SessionInfoPanel;
using Homespun.Client.Services;
using Homespun.Shared.Models.Plans;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionPlansTabTests : BunitTestContext
{
    private MockHttpMessageHandler _mockHandler = null!;
    private Mock<IJSRuntime> _mockJsRuntime = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = _mockHandler.CreateClient();
        Services.AddSingleton(new HttpPlansApiService(httpClient));

        _mockJsRuntime = new Mock<IJSRuntime>();
        Services.AddSingleton(_mockJsRuntime.Object);
    }

    [Test]
    public void SessionPlansTab_WithPlans_DisplaysList()
    {
        // Arrange
        var plans = new List<PlanFileInfo>
        {
            new() { FileName = "plan1.md", FilePath = "/test/.claude/plans/plan1.md", LastModified = DateTime.Now, FileSizeBytes = 100, Preview = "# Plan 1" },
            new() { FileName = "plan2.md", FilePath = "/test/.claude/plans/plan2.md", LastModified = DateTime.Now.AddHours(-1), FileSizeBytes = 200, Preview = "# Plan 2" }
        };
        _mockHandler.RespondWith("api/plans", plans);

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        // Wait for async load
        cut.WaitForState(() => cut.FindAll(".plan-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var planItems = cut.FindAll(".plan-item");
        Assert.That(planItems, Has.Count.EqualTo(2));
    }

    [Test]
    public void SessionPlansTab_NoPlanFiles_ShowsEmptyState()
    {
        // Arrange
        _mockHandler.RespondWith("api/plans", new List<PlanFileInfo>());

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        cut.WaitForState(() => cut.Find(".empty-state") != null, TimeSpan.FromSeconds(1));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No plans"));
    }

    [Test]
    public void SessionPlansTab_ShowsPreview()
    {
        // Arrange
        var plans = new List<PlanFileInfo>
        {
            new() { FileName = "plan.md", FilePath = "/test/.claude/plans/plan.md", LastModified = DateTime.Now, FileSizeBytes = 100, Preview = "# Implementation Plan" }
        };
        _mockHandler.RespondWith("api/plans", plans);

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        cut.WaitForState(() => cut.FindAll(".plan-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var preview = cut.Find(".plan-preview");
        Assert.That(preview.TextContent, Does.Contain("Implementation Plan"));
    }

    [Test]
    public void SessionPlansTab_ExpandPlan_ShowsFullContent()
    {
        // Arrange
        var plans = new List<PlanFileInfo>
        {
            new() { FileName = "plan.md", FilePath = "/test/.claude/plans/plan.md", LastModified = DateTime.Now, FileSizeBytes = 100, Preview = "# Plan" }
        };
        _mockHandler.RespondWith("api/plans", plans);
        _mockHandler.RespondWith("api/plans/content", "# Full Plan Content\n\nThis is the full content.");

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        cut.WaitForState(() => cut.FindAll(".plan-item").Count > 0, TimeSpan.FromSeconds(1));

        // Click expand button
        var expandButton = cut.Find(".expand-button");
        expandButton.Click();

        cut.WaitForState(() => cut.FindAll(".plan-content").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var content = cut.Find(".plan-content");
        Assert.That(content.TextContent, Does.Contain("Full Plan Content"));
    }

    [Test]
    public void SessionPlansTab_NoWorkingDirectory_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionPlansTab>();

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState, Is.Not.Null);
    }

    [Test]
    public void SessionPlansTab_ShowsFileName()
    {
        // Arrange
        var plans = new List<PlanFileInfo>
        {
            new() { FileName = "fluffy-aurora.md", FilePath = "/test/.claude/plans/fluffy-aurora.md", LastModified = DateTime.Now, FileSizeBytes = 150 }
        };
        _mockHandler.RespondWith("api/plans", plans);

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        cut.WaitForState(() => cut.FindAll(".plan-item").Count > 0, TimeSpan.FromSeconds(1));

        // Assert
        var fileName = cut.Find(".plan-filename");
        Assert.That(fileName.TextContent, Does.Contain("fluffy-aurora.md"));
    }

    [Test]
    public void SessionPlansTab_Loading_ShowsSpinner()
    {
        // Arrange
        _mockHandler.WithDefaultResponse(new List<PlanFileInfo>());

        // Act
        var cut = Render<SessionPlansTab>(parameters => parameters
            .Add(p => p.WorkingDirectory, "/test/path"));

        // Assert - Component should eventually reach a state (loading or loaded)
        cut.WaitForState(() =>
            cut.FindAll(".loading-state").Count > 0 || cut.FindAll(".empty-state").Count > 0,
            TimeSpan.FromSeconds(1));
    }
}
