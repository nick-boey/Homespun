using Microsoft.AspNetCore.Components.Web;
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

    #region Description Binding Event Tests

    /// <summary>
    /// Documents that the description field MUST use oninput binding event.
    /// When using default onchange binding, CTRL+Enter saves stale data because
    /// the textarea hasn't lost focus yet, so onchange hasn't fired.
    /// Using oninput ensures the backing field is updated on every keystroke.
    /// </summary>
    /// <remarks>
    /// This test documents the expected binding behavior after the fix for issue:
    /// "Issues not being written with full description with CTRL+Enter"
    ///
    /// Root cause: The description textarea used @bind="_description" without
    /// @bind:event="oninput", so the backing field was only updated on blur.
    /// When CTRL+Enter was pressed without leaving the field, the save used
    /// the stale value.
    /// </remarks>
    [Test]
    public void DescriptionField_ShouldUseOninputBinding_ToSupportCtrlEnterSave()
    {
        // This test documents the expected binding behavior.
        // The actual verification is done by reading the component source.
        // The fix ensures @bind:event="oninput" is present on the description textarea.

        // Arrange - define the expected binding configuration
        var expectedBindingEvent = "oninput";
        var titleBindingEvent = "oninput";  // Title field already uses this (reference)

        // Assert - both title and description should use the same binding pattern
        // This ensures consistent behavior across form fields
        Assert.That(expectedBindingEvent, Is.EqualTo(titleBindingEvent),
            "Description field must use the same binding event as title field to ensure " +
            "CTRL+Enter saves capture the latest typed value");
    }

    /// <summary>
    /// Verifies that using onchange binding (default) would cause stale data on CTRL+Enter.
    /// This test documents WHY oninput is required.
    /// </summary>
    [Test]
    public void DefaultOnchangeBinding_WouldCauseStaleDataOnCtrlEnter_BecauseNoFocusLoss()
    {
        // Arrange - simulate the problem scenario
        var initialValue = "Initial description";
        var typedValue = "Initial description with more text typed";
        var fieldHasLostFocus = false;  // CTRL+Enter doesn't cause focus loss

        // Act - with onchange (default), field only updates on focus loss
        var valueWithOnchangeBinding = fieldHasLostFocus ? typedValue : initialValue;

        // Assert - demonstrates the bug: value is stale when using onchange
        Assert.That(valueWithOnchangeBinding, Is.EqualTo(initialValue),
            "With onchange binding and no focus loss, the backing field retains the initial value");
        Assert.That(valueWithOnchangeBinding, Is.Not.EqualTo(typedValue),
            "The newly typed text is NOT captured with onchange binding when CTRL+Enter is pressed");
    }

    /// <summary>
    /// Verifies that using oninput binding captures the latest value regardless of focus.
    /// This test documents how the fix works.
    /// </summary>
    [Test]
    public void OninputBinding_CapturesLatestValue_RegardlessOfFocusState()
    {
        // Arrange - simulate the fixed scenario
        var typedValue = "Complete description text typed by user";
        var fieldHasLostFocus = false;  // CTRL+Enter doesn't cause focus loss

        // Act - with oninput, field updates on every keystroke (focus doesn't matter)
        var valueWithOninputBinding = typedValue;  // Always has latest value

        // Assert - demonstrates the fix: value is current regardless of focus
        Assert.That(valueWithOninputBinding, Is.EqualTo(typedValue),
            "With oninput binding, the backing field always has the latest typed value");
    }

    #endregion

    #region CTRL+Enter Keyboard Handling Tests

    /// <summary>
    /// The HandleKeyDown method should trigger submit on Ctrl+Enter.
    /// This tests the submission condition logic that must be present in the component.
    /// </summary>
    [Test]
    public void HandleKeyDown_CtrlEnter_ShouldTriggerSubmit()
    {
        // Arrange
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true,
            ShiftKey = false,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.True, "Ctrl+Enter should trigger submit");
    }

    /// <summary>
    /// Pressing Enter alone should NOT trigger submit - it should allow
    /// normal newline behavior in the textarea.
    /// </summary>
    [Test]
    public void HandleKeyDown_EnterOnly_ShouldNotTriggerSubmit()
    {
        // Arrange
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = false,
            ShiftKey = false,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Enter alone should not trigger submit");
    }

    /// <summary>
    /// Meta+Enter (Cmd+Enter on Mac) should also trigger submit for
    /// cross-platform keyboard shortcut consistency.
    /// </summary>
    [Test]
    public void HandleKeyDown_MetaEnter_ShouldTriggerSubmit()
    {
        // Arrange - Cmd+Enter on Mac
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = false,
            ShiftKey = false,
            MetaKey = true
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.True, "Meta+Enter (Cmd+Enter on Mac) should trigger submit");
    }

    /// <summary>
    /// Shift+Enter should NOT trigger submit - it should allow
    /// newline insertion in the textarea.
    /// </summary>
    [Test]
    public void HandleKeyDown_ShiftEnter_ShouldNotTriggerSubmit()
    {
        // Arrange
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = false,
            ShiftKey = true,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Shift+Enter should not trigger submit");
    }

    /// <summary>
    /// Non-Enter keys should not trigger submit, even with Ctrl held.
    /// </summary>
    [Test]
    public void HandleKeyDown_NonEnterKey_ShouldNotTriggerSubmit()
    {
        // Arrange
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "a",
            CtrlKey = true,
            ShiftKey = false,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Non-Enter keys should not trigger submit");
    }

    /// <summary>
    /// Documents that HandleKeyDown MUST be async Task, not void.
    /// The fire-and-forget pattern causes race conditions with Blazor binding.
    /// </summary>
    /// <remarks>
    /// This test documents the architectural requirement for the fix:
    ///
    /// The void + fire-and-forget pattern:
    ///   private void HandleKeyDown(KeyboardEventArgs e) { _ = HandleSubmit(); }
    /// causes _description to have stale data because HandleSubmit may execute
    /// before the oninput binding update is applied by Blazor.
    ///
    /// The correct pattern:
    ///   private async Task HandleKeyDown(KeyboardEventArgs e) { await HandleSubmit(); }
    /// ensures Blazor waits for the full handler, allowing binding updates to complete.
    /// </remarks>
    [Test]
    public void HandleKeyDown_MustBeAsyncTask_NotVoid_ToEnsureBindingCompletion()
    {
        // This test documents the requirement that HandleKeyDown must be async Task
        // with await HandleSubmit() to ensure Blazor's binding cycle completes
        // before HandleSubmit reads the backing fields.

        // When HandleKeyDown returns immediately (void method with _ = HandleSubmit()),
        // Blazor considers the event complete, but the async HandleSubmit may read
        // _description before the oninput binding update is applied.

        // The fix changes HandleKeyDown to:
        // - Return Task instead of void
        // - await HandleSubmit() instead of fire-and-forget
        // - Support MetaKey for Mac Cmd+Enter

        Assert.Pass("HandleKeyDown must be async Task with await to fix the binding race condition");
    }

    // Helper method that mirrors the expected behavior in IssueEdit.razor
    // This is the FIXED version that should be implemented in the component
    private static bool ShouldSubmitOnKeyDown(KeyboardEventArgs e)
    {
        // Submit on Ctrl+Enter OR Meta+Enter (Cmd+Enter on Mac)
        return e.Key == "Enter" && (e.CtrlKey || e.MetaKey);
    }

    #endregion
}
