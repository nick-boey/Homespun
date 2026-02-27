using Fleece.Core.Models;
using Homespun.Client.Services;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for Session.razor prompt context injection behavior.
/// TDD: These tests define the expected behavior for OnPromptSelected using issue data.
///
/// NOTE: Since the Session.razor page is complex with many dependencies,
/// these tests verify the expected behavior through logic/contract testing.
/// </summary>
[TestFixture]
public class SessionPromptContextTests
{
    #region Prompt Context Building Tests

    [Test]
    public void BuildPromptContext_WithIssue_UsesIssueId()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act - simulate building context as OnPromptSelected should
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Id, Is.EqualTo("ABC123"));
    }

    [Test]
    public void BuildPromptContext_WithIssue_UsesIssueTitle()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Title, Is.EqualTo("Fix authentication bug"));
    }

    [Test]
    public void BuildPromptContext_WithIssue_UsesIssueDescription()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Description, Is.EqualTo("OAuth2 implementation needed"));
    }

    [Test]
    public void BuildPromptContext_WithIssue_UsesIssueType()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Type, Is.EqualTo("Bug"));
    }

    [Test]
    public void BuildPromptContext_WithIssue_UsesWorkingBranchId()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Branch, Is.EqualTo("bug/auth+ABC123"));
    }

    [Test]
    public void BuildPromptContext_WithIssue_AllFieldsPopulated()
    {
        // Arrange
        var issue = CreateTestIssue();
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(context.Id, Is.EqualTo("ABC123"));
            Assert.That(context.Title, Is.EqualTo("Fix authentication bug"));
            Assert.That(context.Description, Is.EqualTo("OAuth2 implementation needed"));
            Assert.That(context.Type, Is.EqualTo("Bug"));
            Assert.That(context.Branch, Is.EqualTo("bug/auth+ABC123"));
        });
    }

    #endregion

    #region Fallback Behavior Tests

    [Test]
    public void BuildPromptContext_WithNullIssue_UsesSessionEntityId()
    {
        // Arrange
        IssueResponse? issue = null;
        var session = CreateTestSession(entityId: "entity-123");

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Id, Is.EqualTo("entity-123"));
    }

    [Test]
    public void BuildPromptContext_WithNullIssue_UsesEmptyTitle()
    {
        // Arrange
        IssueResponse? issue = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Title, Is.Empty);
    }

    [Test]
    public void BuildPromptContext_WithNullIssue_UsesNullDescription()
    {
        // Arrange
        IssueResponse? issue = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Description, Is.Null);
    }

    [Test]
    public void BuildPromptContext_WithNullIssue_UsesEmptyType()
    {
        // Arrange
        IssueResponse? issue = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Type, Is.Empty);
    }

    [Test]
    public void BuildPromptContext_WithNullIssue_UsesEmptyBranch()
    {
        // Arrange
        IssueResponse? issue = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Branch, Is.Empty);
    }

    [Test]
    public void BuildPromptContext_ForCloneEntity_UsesCloneEntityId()
    {
        // Clone entities don't have associated issues
        IssueResponse? issue = null;
        var session = CreateTestSession(entityId: "clone:some-clone-id");

        var context = BuildPromptContext(issue, session);

        Assert.That(context.Id, Is.EqualTo("clone:some-clone-id"));
    }

    [Test]
    public void BuildPromptContext_ForCloneEntity_OtherFieldsEmpty()
    {
        // Clone entities should have empty values for other fields
        IssueResponse? issue = null;
        var session = CreateTestSession(entityId: "clone:some-clone-id");

        var context = BuildPromptContext(issue, session);

        Assert.Multiple(() =>
        {
            Assert.That(context.Title, Is.Empty);
            Assert.That(context.Description, Is.Null);
            Assert.That(context.Type, Is.Empty);
            Assert.That(context.Branch, Is.Empty);
        });
    }

    #endregion

    #region Partial Data Tests

    [Test]
    public void BuildPromptContext_WithNullDescription_DescriptionIsNull()
    {
        // Arrange
        var issue = CreateTestIssue();
        issue.Description = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Description, Is.Null);
    }

    [Test]
    public void BuildPromptContext_WithNullWorkingBranchId_BranchIsEmpty()
    {
        // Arrange
        var issue = CreateTestIssue();
        issue.WorkingBranchId = null;
        var session = CreateTestSession();

        // Act
        var context = BuildPromptContext(issue, session);

        // Assert
        Assert.That(context.Branch, Is.Empty);
    }

    #endregion

    #region Template Rendering Tests

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesTitle()
    {
        // Arrange
        var context = new PromptContext
        {
            Id = "ABC123",
            Title = "Fix authentication bug",
            Description = "OAuth2 implementation needed",
            Type = "Bug",
            Branch = "bug/auth+ABC123"
        };
        var template = "Working on {{title}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Working on Fix authentication bug"));
    }

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesId()
    {
        // Arrange
        var context = new PromptContext
        {
            Id = "ABC123",
            Title = "Fix authentication bug"
        };
        var template = "Issue {{id}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Issue ABC123"));
    }

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesDescription()
    {
        // Arrange
        var context = new PromptContext
        {
            Description = "OAuth2 implementation needed"
        };
        var template = "Description: {{description}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Description: OAuth2 implementation needed"));
    }

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesBranch()
    {
        // Arrange
        var context = new PromptContext
        {
            Branch = "bug/auth+ABC123"
        };
        var template = "Branch: {{branch}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Branch: bug/auth+ABC123"));
    }

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesType()
    {
        // Arrange
        var context = new PromptContext
        {
            Type = "Bug"
        };
        var template = "Type: {{type}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Type: Bug"));
    }

    [Test]
    public void RenderTemplate_WithIssueContext_ReplacesAllPlaceholders()
    {
        // Arrange
        var context = new PromptContext
        {
            Id = "ABC123",
            Title = "Fix authentication bug",
            Description = "OAuth2 implementation needed",
            Type = "Bug",
            Branch = "bug/auth+ABC123"
        };
        var template = "Working on {{title}} ({{id}}) - {{type}} on branch {{branch}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo(
            "Working on Fix authentication bug (ABC123) - Bug on branch bug/auth+ABC123"));
    }

    [Test]
    public void RenderTemplate_WithNullDescription_ReplacesWithEmptyString()
    {
        // Arrange
        var context = new PromptContext
        {
            Description = null
        };
        var template = "Description: {{description}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("Description: "));
    }

    [Test]
    public void RenderTemplate_CaseInsensitive_ReplacesPlaceholders()
    {
        // Arrange
        var context = new PromptContext
        {
            Title = "My Title"
        };
        var template = "{{TITLE}} and {{Title}} and {{title}}";

        // Act
        var result = HttpAgentPromptApiService.RenderTemplate(template, context);

        // Assert
        Assert.That(result, Is.EqualTo("My Title and My Title and My Title"));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Mirrors the prompt context building logic that should be in Session.razor OnPromptSelected.
    /// This is the expected implementation.
    /// </summary>
    private static PromptContext BuildPromptContext(IssueResponse? issue, ClaudeSession session)
    {
        return new PromptContext
        {
            Id = issue?.Id ?? session.EntityId ?? string.Empty,
            Title = issue?.Title ?? string.Empty,
            Description = issue?.Description,
            Type = issue?.Type.ToString() ?? string.Empty,
            Branch = issue?.WorkingBranchId ?? string.Empty
        };
    }

    private static IssueResponse CreateTestIssue()
    {
        return new IssueResponse
        {
            Id = "ABC123",
            Title = "Fix authentication bug",
            Description = "OAuth2 implementation needed",
            Type = IssueType.Bug,
            Status = IssueStatus.Open,
            WorkingBranchId = "bug/auth+ABC123",
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ClaudeSession CreateTestSession(string entityId = "ABC123")
    {
        return new ClaudeSession
        {
            Id = "session-1",
            EntityId = entityId,
            ProjectId = "proj-1",
            WorkingDirectory = "/test/clone",
            Mode = SessionMode.Build,
            Model = "sonnet",
            Status = ClaudeSessionStatus.WaitingForInput
        };
    }

    #endregion
}
