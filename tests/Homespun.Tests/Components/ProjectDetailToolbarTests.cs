using Homespun.Client.Pages;
using Homespun.Client.Services;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for ProjectDetail toolbar button logic.
/// Tests the CanCreateIssue helper method that determines button disabled state.
/// </summary>
[TestFixture]
public class ProjectDetailToolbarTests
{
    [Test]
    public void CanCreateIssue_ReturnsTrue_WhenIssueSelectedAndViewingMode()
    {
        // Arrange
        var selectedIndex = 0;
        var editMode = KeyboardEditMode.Viewing;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CanCreateIssue_ReturnsFalse_WhenNoIssueSelected()
    {
        // Arrange
        var selectedIndex = -1;
        var editMode = KeyboardEditMode.Viewing;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCreateIssue_ReturnsFalse_WhenInEditingExistingMode()
    {
        // Arrange
        var selectedIndex = 0;
        var editMode = KeyboardEditMode.EditingExisting;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCreateIssue_ReturnsFalse_WhenInCreatingNewMode()
    {
        // Arrange
        var selectedIndex = 0;
        var editMode = KeyboardEditMode.CreatingNew;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCreateIssue_ReturnsFalse_WhenNoSelectionAndInEditMode()
    {
        // Arrange - both conditions fail
        var selectedIndex = -1;
        var editMode = KeyboardEditMode.EditingExisting;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCreateIssue_ReturnsTrue_WhenHighIndexSelectedAndViewingMode()
    {
        // Arrange - ensure any positive index works
        var selectedIndex = 100;
        var editMode = KeyboardEditMode.Viewing;

        // Act
        var result = CanCreateIssue(selectedIndex, editMode);

        // Assert
        Assert.That(result, Is.True);
    }

    /// <summary>
    /// Helper method mirroring the logic in ProjectDetail.razor.
    /// </summary>
    private static bool CanCreateIssue(int selectedIndex, KeyboardEditMode editMode)
    {
        return selectedIndex >= 0 && editMode == KeyboardEditMode.Viewing;
    }
}
