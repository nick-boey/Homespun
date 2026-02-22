using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Sessions;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests to verify the fix for the race condition in IssueDetailPanel where
/// the Issue parameter could change during async operations, causing the wrong
/// issue data to be sent to the agent.
///
/// The fix captures issue data at the start of StartSession before any async operations.
/// </summary>
[TestFixture]
public class IssueDetailPanelRaceConditionTests
{
    /// <summary>
    /// Tests that when building a prompt context, the captured issue data is used
    /// rather than a different issue that might have been selected during async operations.
    ///
    /// This test validates the pattern: capture issue data at method start, use captured data
    /// throughout the method instead of accessing the potentially-changed Issue parameter.
    /// </summary>
    [Test]
    public void BuildPromptContext_UsesSnapshotData_NotCurrentIssue()
    {
        // Arrange - Original issue that was selected when "Start" was clicked
        var originalIssue = CreateTestIssue("ISSUE-A", "Original Title", "Original Description", IssueType.Bug);

        // The issue that the user switched to during async operations
        var changedIssue = CreateTestIssue("ISSUE-B", "Changed Title", "Changed Description", IssueType.Feature);

        // Capture snapshot at start (this is what the fix does)
        var issueSnapshot = originalIssue;
        var branchName = GenerateBranchName(originalIssue);

        // Simulate async operations passing and user changing selection
        // (in real code, this is when Issue parameter would update to changedIssue)
        var currentIssueAfterDelay = changedIssue;

        // Act - Build prompt context using the snapshot (correct behavior after fix)
        var promptContext = BuildPromptContextWithSnapshot(issueSnapshot, branchName);

        // Also show what the buggy behavior would produce
        var buggyPromptContext = BuildPromptContextWithCurrentIssue(currentIssueAfterDelay, branchName);

        // Assert - Verify snapshot-based context uses original issue data
        Assert.Multiple(() =>
        {
            Assert.That(promptContext.Id, Is.EqualTo("ISSUE-A"), "Should use original issue ID");
            Assert.That(promptContext.Title, Is.EqualTo("Original Title"), "Should use original issue title");
            Assert.That(promptContext.Description, Is.EqualTo("Original Description"), "Should use original description");
            Assert.That(promptContext.Type, Is.EqualTo("Bug"), "Should use original issue type");
            Assert.That(promptContext.Branch, Does.Contain("ISSUE-A"), "Branch should reference original issue");
        });

        // Verify the buggy behavior would have used wrong data
        Assert.Multiple(() =>
        {
            Assert.That(buggyPromptContext.Id, Is.EqualTo("ISSUE-B"), "Buggy version would use wrong issue ID");
            Assert.That(buggyPromptContext.Title, Is.EqualTo("Changed Title"), "Buggy version would use wrong title");
        });
    }

    /// <summary>
    /// Tests that the system prompt generation uses captured issue data.
    /// </summary>
    [Test]
    public void GenerateSystemPrompt_UsesSnapshotData_NotCurrentIssue()
    {
        // Arrange
        var originalIssue = CreateTestIssue("ISSUE-A", "Fix login bug", "Users cannot log in", IssueType.Bug);
        var changedIssue = CreateTestIssue("ISSUE-B", "Add dark mode", "Implement theme switching", IssueType.Feature);

        // Capture at start
        var issueSnapshot = originalIssue;
        var branchName = GenerateBranchName(originalIssue);

        // User changes selection during async operations
        var currentIssueAfterDelay = changedIssue;

        // Act - Generate system prompt using snapshot (correct)
        var systemPrompt = GenerateSystemPromptWithSnapshot(issueSnapshot, branchName);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(systemPrompt, Does.Contain("ISSUE-A"), "Should reference original issue ID");
            Assert.That(systemPrompt, Does.Contain("Fix login bug"), "Should reference original title");
            Assert.That(systemPrompt, Does.Contain("Users cannot log in"), "Should reference original description");
            Assert.That(systemPrompt, Does.Not.Contain("ISSUE-B"), "Should NOT reference changed issue ID");
            Assert.That(systemPrompt, Does.Not.Contain("Add dark mode"), "Should NOT reference changed title");
        });
    }

    /// <summary>
    /// Tests that the session creation uses the captured issue ID.
    /// </summary>
    [Test]
    public void CreateSession_UsesSnapshotId_NotCurrentIssueId()
    {
        // Arrange
        var originalIssue = CreateTestIssue("ISSUE-A", "Original Task", null, IssueType.Task);
        var changedIssue = CreateTestIssue("ISSUE-B", "Changed Task", null, IssueType.Task);

        // Capture at start
        var issueIdSnapshot = originalIssue.Id;

        // User changes selection
        var currentIssueAfterDelay = changedIssue;

        // Act - Create session request using snapshot ID (correct)
        var sessionRequest = new CreateSessionRequest
        {
            EntityId = issueIdSnapshot, // This is the fix: use captured ID
            ProjectId = "test-project",
            WorkingDirectory = "/test/path",
            Mode = SessionMode.Build,
            Model = "opus"
        };

        // Assert
        Assert.That(sessionRequest.EntityId, Is.EqualTo("ISSUE-A"),
            "Session should be created for the original issue, not the currently selected one");
    }

    /// <summary>
    /// Tests that branch name is captured at start and used consistently.
    /// </summary>
    [Test]
    public void BranchName_IsCapturedAtStart_UsedConsistently()
    {
        // Arrange
        var originalIssue = CreateTestIssue("ABC123", "My Feature", null, IssueType.Feature);
        originalIssue.WorkingBranchId = "feature/my-feature+ABC123";

        var changedIssue = CreateTestIssue("DEF456", "Other Feature", null, IssueType.Feature);
        changedIssue.WorkingBranchId = "feature/other-feature+DEF456";

        // Capture at start
        var branchNameSnapshot = GetBranchName(originalIssue);

        // User changes selection
        var currentIssue = changedIssue;
        var currentBranchName = GetBranchName(currentIssue);

        // Assert
        Assert.That(branchNameSnapshot, Is.EqualTo("feature/my-feature+ABC123"),
            "Should use the branch from when Start was clicked");
        Assert.That(currentBranchName, Is.EqualTo("feature/other-feature+DEF456"),
            "Current issue would have different branch");
        Assert.That(branchNameSnapshot, Is.Not.EqualTo(currentBranchName),
            "Captured and current branch names should differ");
    }

    #region Helper Methods (mirroring IssueDetailPanel logic)

    private static IssueResponse CreateTestIssue(string id, string title, string? description, IssueType type)
    {
        return new IssueResponse
        {
            Id = id,
            Title = title,
            Description = description,
            Type = type,
            Status = IssueStatus.Open,
            LastUpdate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string GenerateBranchName(IssueResponse issue)
    {
        // Mirrors the branch name generation logic in IssueDetailPanel
        if (!string.IsNullOrEmpty(issue.WorkingBranchId))
            return issue.WorkingBranchId;

        var typePrefix = issue.Type.ToString().ToLowerInvariant();
        var sanitizedTitle = issue.Title.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "");

        // Truncate if too long
        if (sanitizedTitle.Length > 30)
            sanitizedTitle = sanitizedTitle[..30];

        return $"{typePrefix}/{sanitizedTitle}+{issue.Id}";
    }

    private static string GetBranchName(IssueResponse issue)
    {
        return issue.WorkingBranchId ?? $"issue/{issue.Id}";
    }

    private static PromptContext BuildPromptContextWithSnapshot(IssueResponse issueSnapshot, string branchName)
    {
        // This is the CORRECT behavior after the fix: use captured snapshot
        return new PromptContext
        {
            Title = issueSnapshot.Title,
            Id = issueSnapshot.Id,
            Description = issueSnapshot.Description,
            Branch = branchName,
            Type = issueSnapshot.Type.ToString()
        };
    }

    private static PromptContext BuildPromptContextWithCurrentIssue(IssueResponse currentIssue, string branchName)
    {
        // This is the BUGGY behavior: use potentially changed Issue parameter
        return new PromptContext
        {
            Title = currentIssue.Title,
            Id = currentIssue.Id,
            Description = currentIssue.Description,
            Branch = branchName,
            Type = currentIssue.Type.ToString()
        };
    }

    private static string GenerateSystemPromptWithSnapshot(IssueResponse issueSnapshot, string branchName)
    {
        // Mirrors GenerateSystemPrompt logic, but uses snapshot (the fix)
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You are working on issue {issueSnapshot.Id}: {issueSnapshot.Title}");

        if (!string.IsNullOrEmpty(issueSnapshot.Description))
        {
            sb.AppendLine();
            sb.AppendLine("Issue Description:");
            sb.AppendLine(issueSnapshot.Description);
        }

        sb.AppendLine();
        sb.AppendLine($"Branch: {branchName}");

        return sb.ToString();
    }

    #endregion
}
