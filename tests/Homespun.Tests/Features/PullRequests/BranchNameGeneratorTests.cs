using Fleece.Core.Models;
using Homespun.Features.PullRequests;

namespace Homespun.Tests.Features.PullRequests;

/// <summary>
/// Unit tests for BranchNameGenerator to verify branch name calculation is correct
/// and consistent with the expected format: {type}/{branch-id}+{issue-id}
/// </summary>
[TestFixture]
public class BranchNameGeneratorTests
{
    #region GenerateBranchName Tests

    [Test]
    public void GenerateBranchName_BasicIssue_GeneratesCorrectFormat()
    {
        // Arrange
        var issue = CreateIssue(
            id: "abc123",
            title: "Fix authentication bug",
            type: IssueType.Bug,
            group: null);

        // Act
        var branchName = BranchNameGenerator.GenerateBranchName(issue);

        // Assert
        Assert.That(branchName, Is.EqualTo("bug/fix-authentication-bug+abc123"));
    }

    [Test]
    public void GenerateBranchName_FeatureIssue_GeneratesCorrectFormat()
    {
        // Arrange
        var issue = CreateIssue(
            id: "xyz789",
            title: "Add user dashboard",
            type: IssueType.Feature,
            group: "core"); // Group is now ignored in branch name generation

        // Act
        var branchName = BranchNameGenerator.GenerateBranchName(issue);

        // Assert - group is no longer part of branch name
        Assert.That(branchName, Is.EqualTo("feature/add-user-dashboard+xyz789"));
    }

    [Test]
    public void GenerateBranchName_WithCustomWorkingBranchId_UsesCustomId()
    {
        // Arrange
        var issue = CreateIssue(
            id: "def456",
            title: "Some long title that would be sanitized",
            type: IssueType.Task,
            group: "api",
            workingBranchId: "custom-branch-id");

        // Act
        var branchName = BranchNameGenerator.GenerateBranchName(issue);

        // Assert - group is no longer part of branch name
        Assert.That(branchName, Is.EqualTo("task/custom-branch-id+def456"));
    }

    [Test]
    public void GenerateBranchName_ChoreIssue_GeneratesCorrectFormat()
    {
        // Arrange
        var issue = CreateIssue(
            id: "test1",
            title: "Test issue",
            type: IssueType.Chore,
            group: "  "); // Whitespace-only group (ignored anyway)

        // Act
        var branchName = BranchNameGenerator.GenerateBranchName(issue);

        // Assert
        Assert.That(branchName, Is.EqualTo("chore/test-issue+test1"));
    }

    [Test]
    public void GenerateBranchName_TrimsWhitespace()
    {
        // Arrange
        var issue = CreateIssue(
            id: "trim1",
            title: "Trim test",
            type: IssueType.Feature,
            group: "  api  ", // Group is ignored
            workingBranchId: "  my-branch  ");

        // Act
        var branchName = BranchNameGenerator.GenerateBranchName(issue);

        // Assert - only type, trimmed branch id, and issue id
        Assert.That(branchName, Is.EqualTo("feature/my-branch+trim1"));
    }

    [Test]
    public void GenerateBranchName_AllIssueTypes_ProducesCorrectTypeSegment()
    {
        var testCases = new[]
        {
            (IssueType.Feature, "feature"),
            (IssueType.Bug, "bug"),
            (IssueType.Task, "task"),
            (IssueType.Chore, "chore")
        };

        foreach (var (issueType, expectedType) in testCases)
        {
            var issue = CreateIssue(
                id: "type-test",
                title: "Type test",
                type: issueType,
                group: null);

            var branchName = BranchNameGenerator.GenerateBranchName(issue);

            Assert.That(branchName, Does.StartWith($"{expectedType}/"),
                $"Issue type {issueType} should produce branch starting with '{expectedType}/'");
        }
    }

    #endregion

    #region GenerateBranchNamePreview Tests

    [Test]
    public void GenerateBranchNamePreview_MatchesGenerateBranchName()
    {
        // This test verifies that the preview method produces the same result
        // as the issue-based method when given equivalent inputs

        // Arrange
        var issue = CreateIssue(
            id: "match1",
            title: "Test consistency",
            type: IssueType.Feature,
            group: "ui", // Group is ignored in both methods
            workingBranchId: "custom-id");

        // Act
        var fromIssue = BranchNameGenerator.GenerateBranchName(issue);
        var fromPreview = BranchNameGenerator.GenerateBranchNamePreview(
            issue.Id,
            issue.Type,
            issue.Title,
            issue.WorkingBranchId);

        // Assert
        Assert.That(fromPreview, Is.EqualTo(fromIssue));
    }

    [Test]
    public void GenerateBranchNamePreview_WithNullOptionalParams_UsesDefaults()
    {
        // Act
        var branchName = BranchNameGenerator.GenerateBranchNamePreview(
            "preview1",
            IssueType.Bug,
            "Fix something",
            workingBranchId: null);

        // Assert
        Assert.That(branchName, Is.EqualTo("bug/fix-something+preview1"));
    }

    #endregion

    #region SanitizeForBranch Tests

    [Test]
    public void SanitizeForBranch_ConvertsToLowercase()
    {
        var result = BranchNameGenerator.SanitizeForBranch("UPPERCASE Title");
        Assert.That(result, Is.EqualTo("uppercase-title"));
    }

    [Test]
    public void SanitizeForBranch_ReplacesSpacesWithHyphens()
    {
        var result = BranchNameGenerator.SanitizeForBranch("multiple words here");
        Assert.That(result, Is.EqualTo("multiple-words-here"));
    }

    [Test]
    public void SanitizeForBranch_ReplacesUnderscoresWithHyphens()
    {
        var result = BranchNameGenerator.SanitizeForBranch("snake_case_name");
        Assert.That(result, Is.EqualTo("snake-case-name"));
    }

    [Test]
    public void SanitizeForBranch_RemovesSpecialCharacters()
    {
        var result = BranchNameGenerator.SanitizeForBranch("Fix bug! @user #123");
        Assert.That(result, Is.EqualTo("fix-bug-user-123"));
    }

    [Test]
    public void SanitizeForBranch_CollapsesConsecutiveHyphens()
    {
        var result = BranchNameGenerator.SanitizeForBranch("multiple---hyphens");
        Assert.That(result, Is.EqualTo("multiple-hyphens"));
    }

    [Test]
    public void SanitizeForBranch_TrimsHyphensFromEnds()
    {
        var result = BranchNameGenerator.SanitizeForBranch("-leading and trailing-");
        Assert.That(result, Is.EqualTo("leading-and-trailing"));
    }

    [Test]
    public void SanitizeForBranch_EmptyInput_ReturnsPlaceholder()
    {
        var result = BranchNameGenerator.SanitizeForBranch("");
        Assert.That(result, Is.EqualTo("<title>"));
    }

    [Test]
    public void SanitizeForBranch_WhitespaceOnly_ReturnsPlaceholder()
    {
        var result = BranchNameGenerator.SanitizeForBranch("   ");
        Assert.That(result, Is.EqualTo("<title>"));
    }

    #endregion

    #region Issue Modification Scenario Tests

    /// <summary>
    /// This test demonstrates the bug being fixed: when issue properties change,
    /// the branch name should recalculate to reflect the new values.
    /// </summary>
    [Test]
    public void GenerateBranchName_AfterTypeChange_RecalculatesBranchName()
    {
        // Arrange - Original issue
        var originalIssue = CreateIssue(
            id: "recalc1",
            title: "Improve tool output",
            type: IssueType.Feature,
            group: "core"); // Group is ignored

        var originalBranchName = BranchNameGenerator.GenerateBranchName(originalIssue);
        Assert.That(originalBranchName, Is.EqualTo("feature/improve-tool-output+recalc1"));

        // Act - Simulate issue type change (e.g., user changed from Feature to Bug)
        var modifiedIssue = CreateIssue(
            id: "recalc1", // Same ID
            title: "Improve tool output", // Same title
            type: IssueType.Bug, // Changed type!
            group: "core");

        var newBranchName = BranchNameGenerator.GenerateBranchName(modifiedIssue);

        // Assert - Branch name should reflect the new type
        Assert.That(newBranchName, Is.EqualTo("bug/improve-tool-output+recalc1"));
        Assert.That(newBranchName, Is.Not.EqualTo(originalBranchName));
    }

    /// <summary>
    /// Verifies that changing the title results in a different branch name
    /// (when no custom working branch ID is set).
    /// </summary>
    [Test]
    public void GenerateBranchName_AfterTitleChange_RecalculatesBranchName()
    {
        // Arrange - Original issue
        var originalIssue = CreateIssue(
            id: "ttl1",
            title: "Original title",
            type: IssueType.Task,
            group: null);

        var originalBranchName = BranchNameGenerator.GenerateBranchName(originalIssue);
        Assert.That(originalBranchName, Is.EqualTo("task/original-title+ttl1"));

        // Act - Simulate title change
        var modifiedIssue = CreateIssue(
            id: "ttl1",
            title: "Updated title", // Changed title!
            type: IssueType.Task,
            group: null);

        var newBranchName = BranchNameGenerator.GenerateBranchName(modifiedIssue);

        // Assert - Branch name should reflect the new title
        Assert.That(newBranchName, Is.EqualTo("task/updated-title+ttl1"));
        Assert.That(newBranchName, Is.Not.EqualTo(originalBranchName));
    }

    /// <summary>
    /// Verifies that adding a working branch ID overrides the title-based branch ID.
    /// </summary>
    [Test]
    public void GenerateBranchName_WhenWorkingBranchIdAdded_UsesCustomId()
    {
        // Arrange - Original issue without custom ID
        var originalIssue = CreateIssue(
            id: "wbid1",
            title: "Some task",
            type: IssueType.Task,
            group: null);

        var originalBranchName = BranchNameGenerator.GenerateBranchName(originalIssue);
        Assert.That(originalBranchName, Is.EqualTo("task/some-task+wbid1"));

        // Act - Add custom working branch ID
        var modifiedIssue = CreateIssue(
            id: "wbid1",
            title: "Some task",
            type: IssueType.Task,
            group: null,
            workingBranchId: "my-custom-id"); // Added custom ID!

        var newBranchName = BranchNameGenerator.GenerateBranchName(modifiedIssue);

        // Assert - Branch name should use custom ID
        Assert.That(newBranchName, Is.EqualTo("task/my-custom-id+wbid1"));
        Assert.That(newBranchName, Is.Not.EqualTo(originalBranchName));
    }

    #endregion

    #region Helper Methods

    private static Issue CreateIssue(
        string id,
        string title,
        IssueType type,
        string? group,
        string? workingBranchId = null)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Type = type,
            Status = IssueStatus.Next,
            Group = group,
            WorkingBranchId = workingBranchId,
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
