using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for AgentStatusIndicator component logic.
/// Tests the counting logic for working, question, plan ready, waiting, error, and unknown agents.
/// </summary>
[TestFixture]
public class AgentStatusIndicatorTests
{
    private static readonly HashSet<ClaudeSessionStatus> KnownStatuses = new()
    {
        ClaudeSessionStatus.Starting,
        ClaudeSessionStatus.RunningHooks,
        ClaudeSessionStatus.Running,
        ClaudeSessionStatus.WaitingForInput,
        ClaudeSessionStatus.WaitingForQuestionAnswer,
        ClaudeSessionStatus.WaitingForPlanExecution,
        ClaudeSessionStatus.Stopped,
        ClaudeSessionStatus.Error
    };

    [Test]
    public void CalculateCounts_NoSessions_AllCountsZero()
    {
        // Arrange
        var sessions = new List<ClaudeSession>();

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0));
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateCounts_RunningSessions_CountsAsWorking()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Running)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(1));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateCounts_MultipleWorkingSessions_CountsAll()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Running),
            CreateSession(ClaudeSessionStatus.Running),
            CreateSession(ClaudeSessionStatus.Running)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(3));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(3));
        });
    }

    [Test]
    public void CalculateCounts_WaitingForQuestionAnswerSessions_CountsAsQuestion()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForQuestionAnswer)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(1));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateCounts_MultipleQuestionSessions_CountsAll()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForQuestionAnswer),
            CreateSession(ClaudeSessionStatus.WaitingForQuestionAnswer)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(2));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(2));
        });
    }

    [Test]
    public void CalculateCounts_WaitingForInputSessions_CountsAsWaiting()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForInput)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(1));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateCounts_MultipleWaitingSessions_CountsAll()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForInput),
            CreateSession(ClaudeSessionStatus.WaitingForInput)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(2));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(2));
        });
    }

    [Test]
    public void CalculateCounts_StoppedSessions_NotCounted()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Stopped)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0));
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateCounts_ErrorSessions_CountsAsError()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Error)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0)); // Error sessions are not counted in total active
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(1)); // But they ARE counted separately for error display
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateCounts_MultipleErrorSessions_CountsAll()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Error),
            CreateSession(ClaudeSessionStatus.Error),
            CreateSession(ClaudeSessionStatus.Error)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0)); // Error sessions are not counted in total active
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(3)); // All error sessions are counted
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateCounts_StartingSessions_NotCounted()
    {
        // Arrange - Starting sessions are not yet active
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Starting)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0));
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateCounts_WaitingForPlanExecutionSessions_CountsAsPlanReady()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForPlanExecution)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(1));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(1));
        });
    }

    [Test]
    public void CalculateCounts_MultiplePlanReadySessions_CountsAll()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.WaitingForPlanExecution),
            CreateSession(ClaudeSessionStatus.WaitingForPlanExecution),
            CreateSession(ClaudeSessionStatus.WaitingForPlanExecution)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(3));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(0));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(3));
        });
    }

    [Test]
    public void CalculateCounts_MixedStatuses_CountsCorrectly()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Running),                 // working
            CreateSession(ClaudeSessionStatus.Running),                 // working
            CreateSession(ClaudeSessionStatus.WaitingForQuestionAnswer), // question
            CreateSession(ClaudeSessionStatus.WaitingForPlanExecution),  // plan ready
            CreateSession(ClaudeSessionStatus.WaitingForInput),         // waiting
            CreateSession(ClaudeSessionStatus.WaitingForInput),         // waiting
            CreateSession(ClaudeSessionStatus.Stopped),                 // not counted
            CreateSession(ClaudeSessionStatus.Error),                   // error (counted separately)
            CreateSession(ClaudeSessionStatus.Error),                   // error (counted separately)
            CreateSession(ClaudeSessionStatus.Starting)                 // not counted
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(working, Is.EqualTo(2));
            Assert.That(question, Is.EqualTo(1));
            Assert.That(planReady, Is.EqualTo(1));
            Assert.That(waiting, Is.EqualTo(2));
            Assert.That(error, Is.EqualTo(2));
            Assert.That(unknown, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(6)); // Total active excludes error sessions
        });
    }

    [Test]
    public void CalculateCounts_OnlyInactiveSessions_ReturnsZeroTotalButCountsErrors()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
        {
            CreateSession(ClaudeSessionStatus.Stopped),
            CreateSession(ClaudeSessionStatus.Error),
            CreateSession(ClaudeSessionStatus.Starting)
        };

        // Act
        var (working, question, planReady, waiting, error, unknown, total) = CalculateCounts(sessions);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(0));
            Assert.That(working, Is.EqualTo(0));
            Assert.That(question, Is.EqualTo(0));
            Assert.That(planReady, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(error, Is.EqualTo(1)); // Error is counted separately
            Assert.That(unknown, Is.EqualTo(0));
        });
    }

    [Test]
    public void GetTooltipText_OnlyWorking_ShowsWorkingCount()
    {
        // Arrange & Act
        var result = GetTooltipText(3, 0, 0, 0, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("3 working - Click to view"));
    }

    [Test]
    public void GetTooltipText_OnlyQuestion_ShowsQuestionCount()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 2, 0, 0, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 awaiting response - Click to view"));
    }

    [Test]
    public void GetTooltipText_OnlyPlanReady_ShowsPlanReadyCount()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 0, 2, 0, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 plan ready - Click to view"));
    }

    [Test]
    public void GetTooltipText_OnlyWaiting_ShowsWaitingCount()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 0, 0, 2, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 waiting for input - Click to view"));
    }

    [Test]
    public void GetTooltipText_OnlyError_ShowsErrorCount()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 0, 0, 0, 2, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 in error - Click to view"));
    }

    [Test]
    public void GetTooltipText_UnknownStatus_ShowsUnknownCount()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 0, 0, 0, 0, 3);

        // Assert
        Assert.That(result, Is.EqualTo("3 unknown status - Click to view"));
    }

    [Test]
    public void GetTooltipText_WorkingAndWaiting_ShowsBoth()
    {
        // Arrange & Act
        var result = GetTooltipText(2, 0, 0, 1, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 working, 1 waiting for input - Click to view"));
    }

    [Test]
    public void GetTooltipText_WorkingQuestionAndWaiting_ShowsAll()
    {
        // Arrange & Act
        var result = GetTooltipText(2, 1, 0, 3, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 working, 1 awaiting response, 3 waiting for input - Click to view"));
    }

    [Test]
    public void GetTooltipText_AllStatuses_ShowsAll()
    {
        // Arrange & Act
        var result = GetTooltipText(2, 1, 1, 3, 1, 0);

        // Assert
        Assert.That(result, Is.EqualTo("2 working, 1 awaiting response, 1 plan ready, 3 waiting for input, 1 in error - Click to view"));
    }

    [Test]
    public void GetTooltipText_NoneActive_ReturnsEmptyMessage()
    {
        // Arrange & Act
        var result = GetTooltipText(0, 0, 0, 0, 0, 0);

        // Assert
        Assert.That(result, Is.EqualTo("Click to view"));
    }

    /// <summary>
    /// Helper method that mirrors the component's counting logic.
    /// This is the logic that will be implemented in AgentStatusIndicator.razor.
    /// </summary>
    private static (int working, int question, int planReady, int waiting, int error, int unknown, int total) CalculateCounts(
        IEnumerable<ClaudeSession> sessions)
    {
        var sessionList = sessions.ToList();

        var working = sessionList.Count(s =>
            s.Status == ClaudeSessionStatus.Running);

        var question = sessionList.Count(s =>
            s.Status == ClaudeSessionStatus.WaitingForQuestionAnswer);

        var planReady = sessionList.Count(s =>
            s.Status == ClaudeSessionStatus.WaitingForPlanExecution);

        var waiting = sessionList.Count(s =>
            s.Status == ClaudeSessionStatus.WaitingForInput);

        var error = sessionList.Count(s =>
            s.Status == ClaudeSessionStatus.Error);

        var unknown = sessionList.Count(s =>
            !KnownStatuses.Contains(s.Status));

        return (working, question, planReady, waiting, error, unknown, working + question + planReady + waiting);
    }

    /// <summary>
    /// Helper method that mirrors the component's tooltip generation logic.
    /// </summary>
    private static string GetTooltipText(int workingCount, int questionCount, int planReadyCount, int waitingCount, int errorCount, int unknownCount)
    {
        var parts = new List<string>();
        if (workingCount > 0)
            parts.Add($"{workingCount} working");
        if (questionCount > 0)
            parts.Add($"{questionCount} awaiting response");
        if (planReadyCount > 0)
            parts.Add($"{planReadyCount} plan ready");
        if (waitingCount > 0)
            parts.Add($"{waitingCount} waiting for input");
        if (errorCount > 0)
            parts.Add($"{errorCount} in error");
        if (unknownCount > 0)
            parts.Add($"{unknownCount} unknown status");

        var status = parts.Count > 0 ? string.Join(", ", parts) + " - " : "";
        return $"{status}Click to view";
    }

    /// <summary>
    /// Helper to create test sessions with a specific status.
    /// </summary>
    private static ClaudeSession CreateSession(ClaudeSessionStatus status)
    {
        return new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = "test-entity",
            ProjectId = "test-project",
            WorkingDirectory = "/test",
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }
}
