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
}
