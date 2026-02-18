using Homespun.Shared.Models.Containers;
using Homespun.Shared.Models.Sessions;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for WorkerContainers page logic.
/// Verifies grouping by project and display title logic.
/// </summary>
[TestFixture]
public class WorkerContainersTests
{
    #region Grouping Tests

    [Test]
    public void GroupContainersByProject_EmptyList_ReturnsEmptyGroups()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>();

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GroupContainersByProject_SingleProject_CreatesSingleGroup()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>
        {
            CreateContainer("container-1", "proj-1", "Test Project", "issue-1", "Test Issue")
        };

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ProjectName, Is.EqualTo("Test Project"));
        Assert.That(result[0].ProjectId, Is.EqualTo("proj-1"));
        Assert.That(result[0].Containers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GroupContainersByProject_MultipleProjects_GroupsCorrectly()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>
        {
            CreateContainer("container-1", "proj-1", "Project A", "issue-1", "Issue A1"),
            CreateContainer("container-2", "proj-2", "Project B", "issue-2", "Issue B1"),
            CreateContainer("container-3", "proj-1", "Project A", "issue-3", "Issue A2")
        };

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(g => g.ProjectName), Is.EquivalentTo(new[] { "Project A", "Project B" }));

        var projectA = result.First(g => g.ProjectName == "Project A");
        Assert.That(projectA.Containers, Has.Count.EqualTo(2));

        var projectB = result.First(g => g.ProjectName == "Project B");
        Assert.That(projectB.Containers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GroupContainersByProject_NullProjectId_GroupsUnderUnknown()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>
        {
            CreateContainer("container-1", null, null, "issue-1", "Issue 1"),
            CreateContainer("container-2", "proj-1", "Known Project", "issue-2", "Issue 2")
        };

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(g => g.ProjectName), Is.EquivalentTo(new[] { "Known Project", "Unknown Project" }));

        var unknownGroup = result.First(g => g.ProjectId == "unknown");
        Assert.That(unknownGroup.Containers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GroupContainersByProject_OrdersProjectsAlphabetically()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>
        {
            CreateContainer("container-1", "proj-1", "Zulu Project", "issue-1", "Issue 1"),
            CreateContainer("container-2", "proj-2", "Alpha Project", "issue-2", "Issue 2")
        };

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        Assert.That(result[0].ProjectName, Is.EqualTo("Alpha Project"));
        Assert.That(result[1].ProjectName, Is.EqualTo("Zulu Project"));
    }

    [Test]
    public void GroupContainersByProject_OrdersContainersByIssueTitle()
    {
        // Arrange
        var containers = new List<WorkerContainerDto>
        {
            CreateContainer("container-1", "proj-1", "Project", "issue-1", "Zebra Issue"),
            CreateContainer("container-2", "proj-1", "Project", "issue-2", "Apple Issue"),
            CreateContainer("container-3", "proj-1", "Project", "issue-3", "Mango Issue")
        };

        // Act
        var result = GroupContainersByProject(containers);

        // Assert
        var group = result[0];
        Assert.That(group.Containers[0].IssueTitle, Is.EqualTo("Apple Issue"));
        Assert.That(group.Containers[1].IssueTitle, Is.EqualTo("Mango Issue"));
        Assert.That(group.Containers[2].IssueTitle, Is.EqualTo("Zebra Issue"));
    }

    #endregion

    #region Display Title Tests

    [Test]
    public void GetDisplayTitle_WithIssueTitle_ReturnsIssueTitle()
    {
        // Arrange
        var container = CreateContainer("container-1", "proj-1", "Project", "issue-1", "My Issue Title");

        // Act
        var result = GetDisplayTitle(container);

        // Assert
        Assert.That(result, Is.EqualTo("My Issue Title"));
    }

    [Test]
    public void GetDisplayTitle_WithIssueIdOnly_ReturnsIssueId()
    {
        // Arrange
        var container = CreateContainer("container-1", "proj-1", "Project", "abc123", null);

        // Act
        var result = GetDisplayTitle(container);

        // Assert
        Assert.That(result, Is.EqualTo("abc123"));
    }

    [Test]
    public void GetDisplayTitle_NoIssueInfo_ReturnsContainerName()
    {
        // Arrange
        var container = new WorkerContainerDto
        {
            ContainerId = "container-1",
            ContainerName = "my-container-name",
            WorkingDirectory = "/path/to/dir",
            ProjectId = "proj-1",
            ProjectName = "Project",
            IssueId = null,
            IssueTitle = null,
            SessionStatus = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = GetDisplayTitle(container);

        // Assert
        Assert.That(result, Is.EqualTo("my-container-name"));
    }

    [Test]
    public void GetDisplayTitle_EmptyIssueTitle_UsesIssueId()
    {
        // Arrange
        var container = CreateContainer("container-1", "proj-1", "Project", "issue-123", "");

        // Act
        var result = GetDisplayTitle(container);

        // Assert
        Assert.That(result, Is.EqualTo("issue-123"));
    }

    #endregion

    #region Helper Methods

    private static WorkerContainerDto CreateContainer(
        string containerId,
        string? projectId,
        string? projectName,
        string? issueId,
        string? issueTitle)
    {
        return new WorkerContainerDto
        {
            ContainerId = containerId,
            ContainerName = $"container-{containerId}",
            WorkingDirectory = $"/path/to/{containerId}",
            ProjectId = projectId,
            ProjectName = projectName,
            IssueId = issueId,
            IssueTitle = issueTitle,
            SessionStatus = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Groups containers by project, matching the component's implementation.
    /// </summary>
    private static List<ProjectGroupInfo> GroupContainersByProject(List<WorkerContainerDto> containers)
    {
        return containers
            .GroupBy(c => c.ProjectId ?? "unknown")
            .Select(group => new ProjectGroupInfo
            {
                ProjectId = group.Key,
                ProjectName = group.First().ProjectName ?? "Unknown Project",
                Containers = group.OrderBy(c => c.IssueTitle ?? c.IssueId ?? c.ContainerName).ToList()
            })
            .OrderBy(g => g.ProjectName)
            .ToList();
    }

    /// <summary>
    /// Gets the display title for a container card header.
    /// </summary>
    private static string GetDisplayTitle(WorkerContainerDto container)
    {
        if (!string.IsNullOrEmpty(container.IssueTitle))
            return container.IssueTitle;
        if (!string.IsNullOrEmpty(container.IssueId))
            return container.IssueId;
        return container.ContainerName;
    }

    private class ProjectGroupInfo
    {
        public required string ProjectId { get; set; }
        public required string ProjectName { get; set; }
        public required List<WorkerContainerDto> Containers { get; set; }
    }

    #endregion
}
