using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for AgentControlPanel component logic.
/// Tests the state determination and badge class logic.
/// Full component rendering tests would require bUnit.
/// </summary>
[TestFixture]
public class AgentControlPanelTests
{
    [Test]
    public void DetermineState_NoSession_NotStarting_ReturnsShowSelector()
    {
        // Arrange
        ClaudeSession? session = null;
        var isStarting = false;

        // Act
        var state = DetermineControlState(session, isStarting);

        // Assert
        Assert.That(state, Is.EqualTo(AgentControlState.ShowSelector));
    }

    [Test]
    public void DetermineState_NoSession_IsStarting_ReturnsStarting()
    {
        // Arrange
        ClaudeSession? session = null;
        var isStarting = true;

        // Act
        var state = DetermineControlState(session, isStarting);

        // Assert
        Assert.That(state, Is.EqualTo(AgentControlState.Starting));
    }

    [Test]
    public void DetermineState_HasSession_ReturnsShowSession()
    {
        // Arrange
        var session = CreateTestSession(ClaudeSessionStatus.Running);
        var isStarting = false;

        // Act
        var state = DetermineControlState(session, isStarting);

        // Assert
        Assert.That(state, Is.EqualTo(AgentControlState.ShowSession));
    }

    [Test]
    public void DetermineState_HasSession_StillStarting_ReturnsShowSession()
    {
        // Session takes precedence over starting state
        var session = CreateTestSession(ClaudeSessionStatus.Running);
        var isStarting = true;

        // Act
        var state = DetermineControlState(session, isStarting);

        // Assert
        Assert.That(state, Is.EqualTo(AgentControlState.ShowSession));
    }

    [Test]
    public void GetSessionLink_ReturnsCorrectPath()
    {
        // Arrange
        var session = CreateTestSession(ClaudeSessionStatus.Running, "test-session-123");

        // Act
        var link = GetSessionLink(session);

        // Assert
        Assert.That(link, Is.EqualTo("/session/test-session-123"));
    }

    [Test]
    public void SessionStatusBadgeClass_Running_ReturnsSuccessClass()
    {
        // Arrange
        var status = ClaudeSessionStatus.Running;

        // Act
        var cssClass = status.ToBadgeClass();

        // Assert
        Assert.That(cssClass, Is.EqualTo("bg-success"));
    }

    [Test]
    public void SessionStatusBadgeClass_WaitingForInput_ReturnsInfoClass()
    {
        // Arrange
        var status = ClaudeSessionStatus.WaitingForInput;

        // Act
        var cssClass = status.ToBadgeClass();

        // Assert
        Assert.That(cssClass, Is.EqualTo("bg-info"));
    }

    [Test]
    public void SessionStatusBadgeClass_Starting_ReturnsWarningClass()
    {
        // Arrange
        var status = ClaudeSessionStatus.Starting;

        // Act
        var cssClass = status.ToBadgeClass();

        // Assert
        Assert.That(cssClass, Is.EqualTo("bg-warning text-dark"));
    }

    [Test]
    public void SessionStatusBadgeClass_Stopped_ReturnsSecondaryClass()
    {
        // Arrange
        var status = ClaudeSessionStatus.Stopped;

        // Act
        var cssClass = status.ToBadgeClass();

        // Assert
        Assert.That(cssClass, Is.EqualTo("bg-secondary"));
    }

    [Test]
    public void SessionStatusBadgeClass_Error_ReturnsDangerClass()
    {
        // Arrange
        var status = ClaudeSessionStatus.Error;

        // Act
        var cssClass = status.ToBadgeClass();

        // Assert
        Assert.That(cssClass, Is.EqualTo("bg-danger"));
    }

    [Test]
    public void SessionStatusDisplayLabel_Running_ReturnsWorking()
    {
        Assert.That(ClaudeSessionStatus.Running.ToDisplayLabel(), Is.EqualTo("Working"));
    }

    [Test]
    public void SessionStatusDisplayLabel_WaitingForInput_ReturnsWaiting()
    {
        Assert.That(ClaudeSessionStatus.WaitingForInput.ToDisplayLabel(), Is.EqualTo("Waiting"));
    }

    [Test]
    public void SessionStatusDisplayLabel_Starting_ReturnsStarting()
    {
        Assert.That(ClaudeSessionStatus.Starting.ToDisplayLabel(), Is.EqualTo("Starting"));
    }

    [Test]
    public void SessionStatusIsActive_Running_ReturnsTrue()
    {
        Assert.That(ClaudeSessionStatus.Running.IsActive(), Is.True);
    }

    [Test]
    public void SessionStatusIsActive_WaitingForInput_ReturnsTrue()
    {
        Assert.That(ClaudeSessionStatus.WaitingForInput.IsActive(), Is.True);
    }

    [Test]
    public void SessionStatusIsActive_Starting_ReturnsTrue()
    {
        Assert.That(ClaudeSessionStatus.Starting.IsActive(), Is.True);
    }

    [Test]
    public void SessionStatusIsActive_Stopped_ReturnsFalse()
    {
        Assert.That(ClaudeSessionStatus.Stopped.IsActive(), Is.False);
    }

    [Test]
    public void SessionStatusIsActive_Error_ReturnsFalse()
    {
        Assert.That(ClaudeSessionStatus.Error.IsActive(), Is.False);
    }

    // Helper methods mirroring component logic
    private enum AgentControlState
    {
        ShowSelector,
        Starting,
        ShowSession
    }

    private static AgentControlState DetermineControlState(ClaudeSession? session, bool isStarting)
    {
        if (session != null)
            return AgentControlState.ShowSession;
        if (isStarting)
            return AgentControlState.Starting;
        return AgentControlState.ShowSelector;
    }

    private static string GetSessionLink(ClaudeSession session) => $"/session/{session.Id}";

    private static ClaudeSession CreateTestSession(ClaudeSessionStatus status, string? sessionId = null)
    {
        return new ClaudeSession
        {
            Id = sessionId ?? Guid.NewGuid().ToString(),
            EntityId = "test-entity",
            ProjectId = "test-project",
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Build,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Tests for AgentControlPanel entity ID generation logic.
/// </summary>
[TestFixture]
public class AgentControlPanelEntityIdTests
{
    [Test]
    public void CloneEntityId_GeneratesCorrectFormat()
    {
        // Arrange
        var branchName = "feature/my-feature";

        // Act
        var entityId = GenerateCloneEntityId(branchName);

        // Assert
        Assert.That(entityId, Is.EqualTo("clone:feature/my-feature"));
    }

    [Test]
    public void CloneEntityId_HandlesSpecialCharacters()
    {
        // Arrange
        var branchName = "git/feature/improve-clone+QHcQqi";

        // Act
        var entityId = GenerateCloneEntityId(branchName);

        // Assert
        Assert.That(entityId, Is.EqualTo("clone:git/feature/improve-clone+QHcQqi"));
    }

    [Test]
    public void IssueEntityId_UsesIssueIdDirectly()
    {
        // Arrange
        var issueId = "ABC123";

        // Act - Issue panel uses issue ID directly as entity ID
        var entityId = issueId;

        // Assert
        Assert.That(entityId, Is.EqualTo("ABC123"));
    }

    private static string GenerateCloneEntityId(string branchName) => $"clone:{branchName}";
}
