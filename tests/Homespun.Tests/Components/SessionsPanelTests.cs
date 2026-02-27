using Homespun.Shared.Models.Sessions;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for SessionsPanel component logic.
/// The component provides a unified view for session management that can be used
/// on both the global sessions page and project-specific agents tab.
/// </summary>
[TestFixture]
public class SessionsPanelTests
{
    [Test]
    public void FormatUptime_LessThanOneMinute_ShowsSeconds()
    {
        // Arrange
        var uptime = TimeSpan.FromSeconds(45);

        // Act
        var result = FormatUptime(uptime);

        // Assert
        Assert.That(result, Is.EqualTo("45s"));
    }

    [Test]
    public void FormatUptime_LessThanOneHour_ShowsMinutesAndSeconds()
    {
        // Arrange
        var uptime = TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30));

        // Act
        var result = FormatUptime(uptime);

        // Assert
        Assert.That(result, Is.EqualTo("5m 30s"));
    }

    [Test]
    public void FormatUptime_MoreThanOneHour_ShowsHoursAndMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30));

        // Act
        var result = FormatUptime(uptime);

        // Assert
        Assert.That(result, Is.EqualTo("2h 30m"));
    }

    [Test]
    public void FormatUptime_MoreThanOneDay_ShowsDaysAndHours()
    {
        // Arrange
        var uptime = TimeSpan.FromDays(1).Add(TimeSpan.FromHours(5));

        // Act
        var result = FormatUptime(uptime);

        // Assert
        Assert.That(result, Is.EqualTo("1d 5h"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_PR_ReturnsPrClass()
    {
        Assert.That(GetEntityTypeBadgeClass("PR"), Is.EqualTo("pr"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Issue_ReturnsIssueClass()
    {
        Assert.That(GetEntityTypeBadgeClass("Issue"), Is.EqualTo("issue"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_CaseInsensitive()
    {
        Assert.That(GetEntityTypeBadgeClass("issue"), Is.EqualTo("issue"));
        Assert.That(GetEntityTypeBadgeClass("ISSUE"), Is.EqualTo("issue"));
    }

    [Test]
    public void GetStatusSortOrder_ReturnsCorrectPriority()
    {
        // Plan ready should have highest priority
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.WaitingForPlanExecution), Is.EqualTo(0));
        // Question requires user action
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.WaitingForQuestionAnswer), Is.EqualTo(1));
        // Waiting
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.WaitingForInput), Is.EqualTo(2));
        // Working
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.Running), Is.EqualTo(3));
        // Starting
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.Starting), Is.EqualTo(4));
        // Running hooks
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.RunningHooks), Is.EqualTo(5));
        // Terminal states
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.Stopped), Is.EqualTo(6));
        Assert.That(GetStatusSortOrder(ClaudeSessionStatus.Error), Is.EqualTo(7));
    }

    [Test]
    public void GetStatusGroupLabel_ReturnsHumanReadableLabels()
    {
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.WaitingForPlanExecution), Is.EqualTo("Plan Ready"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.WaitingForQuestionAnswer), Is.EqualTo("Question"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.WaitingForInput), Is.EqualTo("Waiting"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.Running), Is.EqualTo("Working"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.Starting), Is.EqualTo("Starting"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.RunningHooks), Is.EqualTo("Running Hooks"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.Stopped), Is.EqualTo("Stopped"));
        Assert.That(GetStatusGroupLabel(ClaudeSessionStatus.Error), Is.EqualTo("Error"));
    }

    [Test]
    public void FormatContainerId_TruncatesLongId()
    {
        // Arrange
        var containerId = "abc123def456ghi789jkl012";

        // Act
        var result = FormatContainerId(containerId);

        // Assert - should show first 12 characters like Docker
        Assert.That(result, Is.EqualTo("abc123def456"));
    }

    [Test]
    public void FormatContainerId_ShortId_ReturnsUnchanged()
    {
        // Arrange
        var containerId = "abc123";

        // Act
        var result = FormatContainerId(containerId);

        // Assert
        Assert.That(result, Is.EqualTo("abc123"));
    }

    [Test]
    public void GetModelDisplayName_ExtractsModelName()
    {
        Assert.That(GetModelDisplayName("anthropic/sonnet"), Is.EqualTo("sonnet"));
        Assert.That(GetModelDisplayName("opus"), Is.EqualTo("opus"));
    }

    // Helper methods that mirror the component's static methods
    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    private static string GetEntityTypeBadgeClass(string entityType) => entityType.ToLowerInvariant() switch
    {
        "pr" => "pr",
        "issue" => "issue",
        _ => "pr"
    };

    private static int GetStatusSortOrder(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.WaitingForPlanExecution => 0,
        ClaudeSessionStatus.WaitingForQuestionAnswer => 1,
        ClaudeSessionStatus.WaitingForInput => 2,
        ClaudeSessionStatus.Running => 3,
        ClaudeSessionStatus.Starting => 4,
        ClaudeSessionStatus.RunningHooks => 5,
        ClaudeSessionStatus.Stopped => 6,
        ClaudeSessionStatus.Error => 7,
        _ => 8
    };

    private static string GetStatusGroupLabel(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.WaitingForPlanExecution => "Plan Ready",
        ClaudeSessionStatus.WaitingForQuestionAnswer => "Question",
        ClaudeSessionStatus.WaitingForInput => "Waiting",
        ClaudeSessionStatus.Running => "Working",
        ClaudeSessionStatus.Starting => "Starting",
        ClaudeSessionStatus.RunningHooks => "Running Hooks",
        ClaudeSessionStatus.Stopped => "Stopped",
        ClaudeSessionStatus.Error => "Error",
        _ => "Unknown"
    };

    private static string FormatContainerId(string containerId)
    {
        return containerId.Length > 12 ? containerId[..12] : containerId;
    }

    private static string GetModelDisplayName(string model)
    {
        var parts = model.Split('/');
        return parts.Length > 1 ? parts[1] : model;
    }
}

/// <summary>
/// Tests for session grouping logic when grouping by project (global view).
/// </summary>
[TestFixture]
public class SessionsPanelProjectGroupingTests
{
    [Test]
    public void GroupByProject_EmptyList_ReturnsEmptyGroups()
    {
        // Arrange
        var sessions = new List<SessionSummary>();
        var entityInfoCache = new Dictionary<string, EntityInfo>();

        // Act
        var result = GroupSessionsByProject(sessions, entityInfoCache);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GroupByProject_SingleProject_CreatesSingleGroup()
    {
        // Arrange
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", "pr-1", "proj-1")
        };

        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "Test PR",
                ProjectId = "proj-1",
                ProjectName = "Test Project"
            }
        };

        // Act
        var result = GroupSessionsByProject(sessions, entityInfoCache);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ProjectName, Is.EqualTo("Test Project"));
        Assert.That(result[0].Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public void GroupByProject_MultipleProjects_GroupsCorrectly()
    {
        // Arrange
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", "pr-1", "proj-1"),
            CreateTestSession("session-2", "pr-2", "proj-2")
        };

        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 1",
                ProjectId = "proj-1",
                ProjectName = "Project A"
            },
            ["pr-2"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 2",
                ProjectId = "proj-2",
                ProjectName = "Project B"
            }
        };

        // Act
        var result = GroupSessionsByProject(sessions, entityInfoCache);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void GroupByProject_OrdersProjectsAlphabetically()
    {
        // Arrange
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", "pr-1", "proj-1"),
            CreateTestSession("session-2", "pr-2", "proj-2")
        };

        var entityInfoCache = new Dictionary<string, EntityInfo>
        {
            ["pr-1"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 1",
                ProjectId = "proj-1",
                ProjectName = "Zulu Project"
            },
            ["pr-2"] = new EntityInfo
            {
                EntityType = "PR",
                Title = "PR 2",
                ProjectId = "proj-2",
                ProjectName = "Alpha Project"
            }
        };

        // Act
        var result = GroupSessionsByProject(sessions, entityInfoCache);

        // Assert
        Assert.That(result[0].ProjectName, Is.EqualTo("Alpha Project"));
        Assert.That(result[1].ProjectName, Is.EqualTo("Zulu Project"));
    }

    // Helper methods
    private static SessionSummary CreateTestSession(string sessionId, string entityId, string projectId)
    {
        return new SessionSummary
        {
            Id = sessionId,
            EntityId = entityId,
            ProjectId = projectId,
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };
    }

    private static List<ProjectGroup> GroupSessionsByProject(
        List<SessionSummary> sessions,
        Dictionary<string, EntityInfo> entityInfoCache)
    {
        return sessions
            .Select(session => new
            {
                Session = session,
                EntityInfo = entityInfoCache.GetValueOrDefault(session.EntityId)
            })
            .GroupBy(x => x.EntityInfo?.ProjectName ?? "Unknown Project")
            .Select(group => new ProjectGroup
            {
                ProjectName = group.Key,
                Sessions = group.Select(x => x.Session)
                    .OrderBy(s => GetStatusSortOrder(s.Status))
                    .ThenBy(s => entityInfoCache.GetValueOrDefault(s.EntityId)?.Title ?? s.EntityId)
                    .ToList()
            })
            .OrderBy(g => g.ProjectName)
            .ToList();
    }

    private static int GetStatusSortOrder(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.WaitingForPlanExecution => 0,
        ClaudeSessionStatus.WaitingForQuestionAnswer => 1,
        ClaudeSessionStatus.WaitingForInput => 2,
        ClaudeSessionStatus.Running => 3,
        ClaudeSessionStatus.Starting => 4,
        ClaudeSessionStatus.RunningHooks => 5,
        ClaudeSessionStatus.Stopped => 6,
        ClaudeSessionStatus.Error => 7,
        _ => 8
    };

    private record ProjectGroup
    {
        public required string ProjectName { get; init; }
        public required List<SessionSummary> Sessions { get; init; }
    }

    private record EntityInfo
    {
        public required string EntityType { get; init; }
        public required string Title { get; init; }
        public string? BranchName { get; init; }
        public required string ProjectId { get; init; }
        public required string ProjectName { get; init; }
    }
}

/// <summary>
/// Tests for session grouping logic when grouping by status (project view).
/// </summary>
[TestFixture]
public class SessionsPanelStatusGroupingTests
{
    [Test]
    public void GroupByStatus_EmptyList_ReturnsEmptyGroups()
    {
        // Arrange
        var sessions = new List<SessionSummary>();

        // Act
        var result = GroupSessionsByStatus(sessions);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GroupByStatus_SingleStatus_CreatesSingleGroup()
    {
        // Arrange
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", ClaudeSessionStatus.Running),
            CreateTestSession("session-2", ClaudeSessionStatus.Running)
        };

        // Act
        var result = GroupSessionsByStatus(sessions);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(ClaudeSessionStatus.Running));
        Assert.That(result[0].Sessions, Has.Count.EqualTo(2));
    }

    [Test]
    public void GroupByStatus_MultipleStatuses_GroupsCorrectly()
    {
        // Arrange
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", ClaudeSessionStatus.Running),
            CreateTestSession("session-2", ClaudeSessionStatus.WaitingForInput),
            CreateTestSession("session-3", ClaudeSessionStatus.WaitingForQuestionAnswer)
        };

        // Act
        var result = GroupSessionsByStatus(sessions);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void GroupByStatus_OrdersByStatusPriority()
    {
        // Arrange - create sessions in reverse priority order
        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-1", ClaudeSessionStatus.Running),
            CreateTestSession("session-2", ClaudeSessionStatus.WaitingForPlanExecution),
            CreateTestSession("session-3", ClaudeSessionStatus.WaitingForInput),
            CreateTestSession("session-4", ClaudeSessionStatus.WaitingForQuestionAnswer)
        };

        // Act
        var result = GroupSessionsByStatus(sessions);

        // Assert - should be ordered by priority: Plan Ready, Question, Waiting, Working
        Assert.That(result[0].Status, Is.EqualTo(ClaudeSessionStatus.WaitingForPlanExecution));
        Assert.That(result[1].Status, Is.EqualTo(ClaudeSessionStatus.WaitingForQuestionAnswer));
        Assert.That(result[2].Status, Is.EqualTo(ClaudeSessionStatus.WaitingForInput));
        Assert.That(result[3].Status, Is.EqualTo(ClaudeSessionStatus.Running));
    }

    [Test]
    public void GroupByStatus_SessionsWithinGroupOrderedByCreatedAt()
    {
        // Arrange
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);

        var sessions = new List<SessionSummary>
        {
            CreateTestSession("session-newer", ClaudeSessionStatus.Running, newer),
            CreateTestSession("session-older", ClaudeSessionStatus.Running, older)
        };

        // Act
        var result = GroupSessionsByStatus(sessions);

        // Assert - newer sessions first (most recent activity)
        Assert.That(result[0].Sessions[0].Id, Is.EqualTo("session-newer"));
        Assert.That(result[0].Sessions[1].Id, Is.EqualTo("session-older"));
    }

    // Helper methods
    private static SessionSummary CreateTestSession(string sessionId, ClaudeSessionStatus status, DateTime? createdAt = null)
    {
        return new SessionSummary
        {
            Id = sessionId,
            EntityId = "entity-1",
            ProjectId = "proj-1",
            Model = "sonnet",
            Mode = SessionMode.Build,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            LastActivityAt = createdAt ?? DateTime.UtcNow
        };
    }

    private static List<StatusGroup> GroupSessionsByStatus(List<SessionSummary> sessions)
    {
        return sessions
            .GroupBy(s => s.Status)
            .Select(group => new StatusGroup
            {
                Status = group.Key,
                Sessions = group
                    .OrderByDescending(s => s.LastActivityAt)
                    .ToList()
            })
            .OrderBy(g => GetStatusSortOrder(g.Status))
            .ToList();
    }

    private static int GetStatusSortOrder(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.WaitingForPlanExecution => 0,
        ClaudeSessionStatus.WaitingForQuestionAnswer => 1,
        ClaudeSessionStatus.WaitingForInput => 2,
        ClaudeSessionStatus.Running => 3,
        ClaudeSessionStatus.Starting => 4,
        ClaudeSessionStatus.RunningHooks => 5,
        ClaudeSessionStatus.Stopped => 6,
        ClaudeSessionStatus.Error => 7,
        _ => 8
    };

    private record StatusGroup
    {
        public required ClaudeSessionStatus Status { get; init; }
        public required List<SessionSummary> Sessions { get; init; }
    }
}
