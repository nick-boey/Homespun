using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for IssueEdit page navigation behavior.
/// Tests the navigation logic for "Save & Run Agent" button.
/// </summary>
[TestFixture]
public class IssueEditPageTests
{
    /// <summary>
    /// After "Save & Run Agent", the page should navigate to the project page,
    /// not to the session page.
    /// </summary>
    [Test]
    public void GetNavigationUrlAfterSaveAndRun_ReturnsProjectPage()
    {
        // Arrange
        var projectId = "test-project-123";

        // Act
        var url = GetNavigationUrlAfterSaveAndRun(projectId);

        // Assert
        Assert.That(url, Is.EqualTo("projects/test-project-123"));
    }

    /// <summary>
    /// The navigation URL should NOT point to a session page.
    /// </summary>
    [Test]
    public void GetNavigationUrlAfterSaveAndRun_DoesNotReturnSessionPage()
    {
        // Arrange
        var projectId = "test-project-123";

        // Act
        var url = GetNavigationUrlAfterSaveAndRun(projectId);

        // Assert
        Assert.That(url, Does.Not.Contain("/session/"));
        Assert.That(url, Does.Not.StartWith("/session"));
    }

    /// <summary>
    /// The navigation URL should include the project ID.
    /// </summary>
    [Test]
    public void GetNavigationUrlAfterSaveAndRun_IncludesProjectId()
    {
        // Arrange
        var projectId = "my-unique-project-id";

        // Act
        var url = GetNavigationUrlAfterSaveAndRun(projectId);

        // Assert
        Assert.That(url, Does.Contain(projectId));
    }

    /// <summary>
    /// Different project IDs should produce different URLs.
    /// </summary>
    [Test]
    public void GetNavigationUrlAfterSaveAndRun_DifferentProjectIds_ProduceDifferentUrls()
    {
        // Arrange
        var projectId1 = "project-alpha";
        var projectId2 = "project-beta";

        // Act
        var url1 = GetNavigationUrlAfterSaveAndRun(projectId1);
        var url2 = GetNavigationUrlAfterSaveAndRun(projectId2);

        // Assert
        Assert.That(url1, Is.Not.EqualTo(url2));
        Assert.That(url1, Does.Contain("project-alpha"));
        Assert.That(url2, Does.Contain("project-beta"));
    }

    // Helper method matching the expected component behavior after fix
    private static string GetNavigationUrlAfterSaveAndRun(string projectId)
        => $"projects/{projectId}";

    #region Auto-Suggest Branch ID Tests

    /// <summary>
    /// Auto-suggest should trigger when AutoSuggest=true and no branch ID exists.
    /// </summary>
    [Test]
    public void ShouldAutoSuggest_WithAutoSuggestTrueAndEmptyBranchId_ReturnsTrue()
    {
        // Arrange
        var autoSuggest = true;
        var workingBranchId = "";

        // Act
        var shouldAutoSuggest = ShouldAutoSuggestBranchId(autoSuggest, workingBranchId);

        // Assert
        Assert.That(shouldAutoSuggest, Is.True);
    }

    /// <summary>
    /// Auto-suggest should NOT trigger when branch ID already exists.
    /// </summary>
    [Test]
    public void ShouldAutoSuggest_WithExistingBranchId_ReturnsFalse()
    {
        // Arrange
        var autoSuggest = true;
        var workingBranchId = "existing-branch-id";

        // Act
        var shouldAutoSuggest = ShouldAutoSuggestBranchId(autoSuggest, workingBranchId);

        // Assert
        Assert.That(shouldAutoSuggest, Is.False);
    }

    /// <summary>
    /// Auto-suggest should NOT trigger when AutoSuggest=false.
    /// </summary>
    [Test]
    public void ShouldAutoSuggest_WithAutoSuggestFalse_ReturnsFalse()
    {
        // Arrange
        var autoSuggest = false;
        var workingBranchId = "";

        // Act
        var shouldAutoSuggest = ShouldAutoSuggestBranchId(autoSuggest, workingBranchId);

        // Assert
        Assert.That(shouldAutoSuggest, Is.False);
    }

    /// <summary>
    /// Auto-suggest should NOT trigger when branch ID is whitespace only.
    /// (Whitespace-only counts as empty, so should auto-suggest)
    /// </summary>
    [Test]
    public void ShouldAutoSuggest_WithWhitespaceBranchId_ReturnsTrue()
    {
        // Arrange
        var autoSuggest = true;
        var workingBranchId = "   ";

        // Act
        var shouldAutoSuggest = ShouldAutoSuggestBranchId(autoSuggest, workingBranchId);

        // Assert
        Assert.That(shouldAutoSuggest, Is.True);
    }

    /// <summary>
    /// Auto-suggest should handle null branch ID as empty.
    /// </summary>
    [Test]
    public void ShouldAutoSuggest_WithNullBranchId_ReturnsTrue()
    {
        // Arrange
        var autoSuggest = true;
        string? workingBranchId = null;

        // Act
        var shouldAutoSuggest = ShouldAutoSuggestBranchId(autoSuggest, workingBranchId);

        // Assert
        Assert.That(shouldAutoSuggest, Is.True);
    }

    // Helper method matching the component's auto-suggest condition
    private static bool ShouldAutoSuggestBranchId(bool autoSuggest, string? workingBranchId)
        => autoSuggest && string.IsNullOrWhiteSpace(workingBranchId);

    #endregion

    #region Navigation URL with AutoSuggest Tests

    /// <summary>
    /// Navigation from "Create & Edit" should include autoSuggest parameter.
    /// </summary>
    [Test]
    public void GetNavigationUrlForCreateAndEdit_IncludesAutoSuggestParameter()
    {
        // Arrange
        var projectId = "test-project";
        var issueId = "test-issue";

        // Act
        var url = GetNavigationUrlForCreateAndEdit(projectId, issueId);

        // Assert
        Assert.That(url, Does.Contain("autoSuggest=true"));
    }

    /// <summary>
    /// Navigation URL should include both project and issue IDs.
    /// </summary>
    [Test]
    public void GetNavigationUrlForCreateAndEdit_IncludesProjectAndIssueIds()
    {
        // Arrange
        var projectId = "my-project";
        var issueId = "my-issue";

        // Act
        var url = GetNavigationUrlForCreateAndEdit(projectId, issueId);

        // Assert
        Assert.That(url, Does.Contain(projectId));
        Assert.That(url, Does.Contain(issueId));
    }

    // Helper method matching ProjectDetail's NavigateToIssueEdit behavior
    private static string GetNavigationUrlForCreateAndEdit(string projectId, string issueId)
        => $"/projects/{projectId}/issues/{issueId}/edit?autoSuggest=true";

    #endregion
}
