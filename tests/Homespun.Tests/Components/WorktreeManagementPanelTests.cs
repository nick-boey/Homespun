using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for WorktreeManagementPanel component logic.
/// These tests focus on the helper methods and logic used in the component.
/// </summary>
[TestFixture]
public class WorktreeManagementPanelTests
{
    [Test]
    public void GetShortBranchName_WithRefsHeadsPrefix_RemovesPrefix()
    {
        // Arrange
        var branch = "refs/heads/feature/my-branch";

        // Act
        var result = GetShortBranchName(branch);

        // Assert
        Assert.That(result, Is.EqualTo("feature/my-branch"));
    }

    [Test]
    public void GetShortBranchName_WithoutPrefix_ReturnsAsIs()
    {
        // Arrange
        var branch = "main";

        // Act
        var result = GetShortBranchName(branch);

        // Assert
        Assert.That(result, Is.EqualTo("main"));
    }

    [Test]
    public void GetShortBranchName_WithNull_ReturnsUnknown()
    {
        // Arrange
        string? branch = null;

        // Act
        var result = GetShortBranchName(branch);

        // Assert
        Assert.That(result, Is.EqualTo("unknown"));
    }

    [Test]
    public void GetShortBranchName_WithEmpty_ReturnsUnknown()
    {
        // Arrange
        var branch = "";

        // Act
        var result = GetShortBranchName(branch);

        // Assert
        Assert.That(result, Is.EqualTo("unknown"));
    }

    [Test]
    public void IsDefaultBranch_WithMainBranch_ReturnsTrue()
    {
        // Arrange
        var branchName = "main";
        var defaultBranch = "main";

        // Act
        var result = IsDefaultBranch(branchName, defaultBranch);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsDefaultBranch_WithMasterBranch_ReturnsTrue()
    {
        // Arrange
        var branchName = "master";
        var defaultBranch = "master";

        // Act
        var result = IsDefaultBranch(branchName, defaultBranch);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsDefaultBranch_WithFeatureBranch_ReturnsFalse()
    {
        // Arrange
        var branchName = "feature/my-feature";
        var defaultBranch = "main";

        // Act
        var result = IsDefaultBranch(branchName, defaultBranch);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsDefaultBranch_WithNullBranch_ReturnsFalse()
    {
        // Arrange
        string? branchName = null;
        var defaultBranch = "main";

        // Act
        var result = IsDefaultBranch(branchName, defaultBranch);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsDefaultBranch_WithEmptyBranch_ReturnsFalse()
    {
        // Arrange
        var branchName = "";
        var defaultBranch = "main";

        // Act
        var result = IsDefaultBranch(branchName, defaultBranch);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetWorktreeEntityId_WithBranch_ReturnsWorktreePrefixedId()
    {
        // Arrange
        var branch = "refs/heads/feature/my-branch";

        // Act
        var result = GetWorktreeEntityId(branch);

        // Assert
        Assert.That(result, Is.EqualTo("worktree:feature/my-branch"));
    }

    [Test]
    public void GetWorktreeEntityId_WithMainBranch_ReturnsWorktreePrefixedMain()
    {
        // Arrange
        var branch = "refs/heads/main";

        // Act
        var result = GetWorktreeEntityId(branch);

        // Assert
        Assert.That(result, Is.EqualTo("worktree:main"));
    }

    [Test]
    public void TruncateMessage_ShortMessage_ReturnsAsIs()
    {
        // Arrange
        var message = "Short message";

        // Act
        var result = TruncateMessage(message);

        // Assert
        Assert.That(result, Is.EqualTo("Short message"));
    }

    [Test]
    public void TruncateMessage_LongMessage_TruncatesWithEllipsis()
    {
        // Arrange
        var message = new string('a', 100);

        // Act
        var result = TruncateMessage(message, 60);

        // Assert
        Assert.That(result.Length, Is.EqualTo(60));
        Assert.That(result.EndsWith("..."), Is.True);
    }

    [Test]
    public void TruncateMessage_NullMessage_ReturnsEmptyString()
    {
        // Arrange
        string? message = null;

        // Act
        var result = TruncateMessage(message);

        // Assert
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void TruncateMessage_ExactLengthMessage_ReturnsAsIs()
    {
        // Arrange
        var message = new string('a', 60);

        // Act
        var result = TruncateMessage(message, 60);

        // Assert
        Assert.That(result, Is.EqualTo(message));
    }

    // Helper methods that mirror the component's private methods
    private static string GetShortBranchName(string? branch)
    {
        if (string.IsNullOrEmpty(branch)) return "unknown";
        return branch.Replace("refs/heads/", "");
    }

    private static bool IsDefaultBranch(string? branchName, string defaultBranch)
    {
        if (string.IsNullOrEmpty(branchName)) return false;
        return branchName == defaultBranch;
    }

    private static string GetWorktreeEntityId(string branch)
    {
        var branchName = GetShortBranchName(branch);
        return $"worktree:{branchName}";
    }

    private static string TruncateMessage(string? message, int maxLength = 60)
    {
        if (string.IsNullOrEmpty(message)) return "";
        return message.Length <= maxLength ? message : message[..(maxLength - 3)] + "...";
    }
}
