using Homespun.Features.ClaudeCode.Data;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Unit tests for AgentManagementPanel component logic.
/// Note: These tests focus on the data transformation and grouping logic.
/// Full component rendering tests would require bUnit.
/// </summary>
[TestFixture]
public class AgentManagementPanelTests
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
    public void FormatUptime_ExactlyOneHour_ShowsHoursAndZeroMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(1);

        // Act
        var result = FormatUptime(uptime);

        // Assert
        Assert.That(result, Is.EqualTo("1h 0m"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_PR_ReturnsPrClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("PR");

        // Assert
        Assert.That(result, Is.EqualTo("pr"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Issue_ReturnsIssueClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("Issue");

        // Assert
        Assert.That(result, Is.EqualTo("issue"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_Unknown_ReturnsPrClass()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("Unknown");

        // Assert
        Assert.That(result, Is.EqualTo("pr"));
    }

    [Test]
    public void GetEntityTypeBadgeClass_CaseInsensitive()
    {
        // Arrange & Act
        var result = GetEntityTypeBadgeClass("issue");

        // Assert
        Assert.That(result, Is.EqualTo("issue"));
    }

    [Test]
    public void GetStatusIndicatorClass_Running_ReturnsRunningClass()
    {
        // Arrange & Act
        var result = GetStatusIndicatorClass(ClaudeSessionStatus.Running);

        // Assert
        Assert.That(result, Is.EqualTo("running"));
    }

    [Test]
    public void GetStatusIndicatorClass_Processing_ReturnsProcessingClass()
    {
        // Arrange & Act
        var result = GetStatusIndicatorClass(ClaudeSessionStatus.Processing);

        // Assert
        Assert.That(result, Is.EqualTo("processing"));
    }

    [Test]
    public void GetStatusIndicatorClass_WaitingForInput_ReturnsWaitingClass()
    {
        // Arrange & Act
        var result = GetStatusIndicatorClass(ClaudeSessionStatus.WaitingForInput);

        // Assert
        Assert.That(result, Is.EqualTo("waiting"));
    }

    [Test]
    public void GetStatusIndicatorClass_Stopped_ReturnsStoppedClass()
    {
        // Arrange & Act
        var result = GetStatusIndicatorClass(ClaudeSessionStatus.Stopped);

        // Assert
        Assert.That(result, Is.EqualTo("stopped"));
    }

    [Test]
    public void GetStatusIndicatorClass_Error_ReturnsErrorClass()
    {
        // Arrange & Act
        var result = GetStatusIndicatorClass(ClaudeSessionStatus.Error);

        // Assert
        Assert.That(result, Is.EqualTo("error"));
    }

    // Helper methods that mirror the component's private methods
    private static string FormatUptime(TimeSpan uptime)
    {
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

    private static string GetStatusIndicatorClass(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.Running => "running",
        ClaudeSessionStatus.Processing => "processing",
        ClaudeSessionStatus.WaitingForInput => "waiting",
        ClaudeSessionStatus.Starting => "processing",
        ClaudeSessionStatus.Stopped => "stopped",
        ClaudeSessionStatus.Error => "error",
        _ => "stopped"
    };
}

/// <summary>
/// Tests for project grouping logic.
/// </summary>
[TestFixture]
public class AgentManagementPanelGroupingTests
{
    [Test]
    public void GroupSessionsByProject_EmptyList_ReturnsEmptyGroups()
    {
        // Arrange
        var sessions = new List<ClaudeSession>();
        var entityInfoCache = new Dictionary<string, EntityInfo>();

        // Act
        var result = GroupSessionsByProject(sessions, entityInfoCache);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GroupSessionsByProject_SingleProject_CreatesSingleGroup()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
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
    public void GroupSessionsByProject_MultipleProjects_GroupsCorrectly()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
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
        Assert.That(result.Select(g => g.ProjectName), Is.EquivalentTo(new[] { "Project A", "Project B" }));
    }

    [Test]
    public void GroupSessionsByProject_OrdersProjectsAlphabetically()
    {
        // Arrange
        var sessions = new List<ClaudeSession>
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
    private static ClaudeSession CreateTestSession(string sessionId, string entityId, string projectId)
    {
        return new ClaudeSession
        {
            Id = sessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = "/test/path",
            Model = "claude-sonnet-4-20250514",
            Mode = SessionMode.Build,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static List<ProjectGroupInfo> GroupSessionsByProject(
        List<ClaudeSession> sessions,
        Dictionary<string, EntityInfo> entityInfoCache)
    {
        return sessions
            .Select(session => new
            {
                Session = session,
                EntityInfo = entityInfoCache.GetValueOrDefault(session.EntityId)
            })
            .GroupBy(x => x.EntityInfo?.ProjectName ?? "Unknown Project")
            .Select(group => new ProjectGroupInfo
            {
                ProjectName = group.Key,
                Sessions = group.Select(x => x.Session)
                    .OrderBy(s => entityInfoCache.GetValueOrDefault(s.EntityId)?.Title ?? s.EntityId)
                    .ToList()
            })
            .OrderBy(g => g.ProjectName)
            .ToList();
    }

    private class ProjectGroupInfo
    {
        public required string ProjectName { get; set; }
        public required List<ClaudeSession> Sessions { get; set; }
    }

    private class EntityInfo
    {
        public required string EntityType { get; set; }
        public required string Title { get; set; }
        public string? BranchName { get; set; }
        public required string ProjectId { get; set; }
        public required string ProjectName { get; set; }
    }
}
