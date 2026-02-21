using Fleece.Core.Models;
using Homespun.Features.Testing.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class MockFleeceServiceTests
{
    private MockFleeceService _service = null!;
    private const string ProjectPath = "/test/project";

    [SetUp]
    public void SetUp()
    {
        var mockLogger = new Mock<ILogger<MockFleeceService>>();
        _service = new MockFleeceService(mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Clear();
    }

    #region Create and Retrieve

    [Test]
    public async Task CreateIssueAsync_ReturnsCreatedIssue()
    {
        var issue = await _service.CreateIssueAsync(ProjectPath, "Test Issue", IssueType.Task);

        Assert.That(issue, Is.Not.Null);
        Assert.That(issue.Title, Is.EqualTo("Test Issue"));
        Assert.That(issue.Type, Is.EqualTo(IssueType.Task));
        Assert.That(issue.Status, Is.EqualTo(IssueStatus.Open));
        Assert.That(issue.Id, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task GetIssueAsync_ReturnsCreatedIssue()
    {
        var created = await _service.CreateIssueAsync(ProjectPath, "Test Issue", IssueType.Task);

        var retrieved = await _service.GetIssueAsync(ProjectPath, created.Id);

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Id, Is.EqualTo(created.Id));
        Assert.That(retrieved.Title, Is.EqualTo("Test Issue"));
    }

    [Test]
    public async Task ListIssuesAsync_ReturnsCreatedIssue()
    {
        var created = await _service.CreateIssueAsync(ProjectPath, "Test Issue", IssueType.Task);

        var issues = await _service.ListIssuesAsync(ProjectPath);

        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Id, Is.EqualTo(created.Id));
    }

    [Test]
    public async Task CreateIssueAsync_MultipleIssues_AllReturned()
    {
        await _service.CreateIssueAsync(ProjectPath, "Issue 1", IssueType.Task);
        await _service.CreateIssueAsync(ProjectPath, "Issue 2", IssueType.Bug);
        await _service.CreateIssueAsync(ProjectPath, "Issue 3", IssueType.Feature);

        var issues = await _service.ListIssuesAsync(ProjectPath);

        Assert.That(issues, Has.Count.EqualTo(3));
    }

    #endregion

    #region Update Persistence

    [Test]
    public async Task UpdateIssueAsync_PersistsChanges()
    {
        var created = await _service.CreateIssueAsync(ProjectPath, "Original", IssueType.Task);

        await _service.UpdateIssueAsync(ProjectPath, created.Id, title: "Updated", status: IssueStatus.Progress);

        var retrieved = await _service.GetIssueAsync(ProjectPath, created.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Title, Is.EqualTo("Updated"));
        Assert.That(retrieved.Status, Is.EqualTo(IssueStatus.Progress));
    }

    #endregion

    #region Delete

    [Test]
    public async Task DeleteIssueAsync_ExcludesFromDefaultList()
    {
        var created = await _service.CreateIssueAsync(ProjectPath, "To Delete", IssueType.Task);

        var deleted = await _service.DeleteIssueAsync(ProjectPath, created.Id);

        Assert.That(deleted, Is.True);

        var issues = await _service.ListIssuesAsync(ProjectPath);
        Assert.That(issues, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task DeleteIssueAsync_StillRetrievableById()
    {
        var created = await _service.CreateIssueAsync(ProjectPath, "To Delete", IssueType.Task);
        await _service.DeleteIssueAsync(ProjectPath, created.Id);

        var retrieved = await _service.GetIssueAsync(ProjectPath, created.Id);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Status, Is.EqualTo(IssueStatus.Deleted));
    }

    #endregion

    #region AddParent Preserves Fields

    [Test]
    public async Task AddParentAsync_PreservesAllFields()
    {
        var parent = await _service.CreateIssueAsync(ProjectPath, "Parent", IssueType.Feature);

        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "child-001",
            Title = "Child",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            Priority = 2,
            Tags = ["tag1", "tag2"],
            LinkedIssues = ["linked-1"],
            LinkedPR = 42,
            CreatedBy = "tester",
            AssignedTo = "dev",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        var updated = await _service.AddParentAsync(ProjectPath, "child-001", parent.Id);

        Assert.That(updated.Tags, Is.EqualTo(new[] { "tag1", "tag2" }));
        Assert.That(updated.LinkedIssues, Is.EqualTo(new[] { "linked-1" }));
        Assert.That(updated.LinkedPR, Is.EqualTo(42));
        Assert.That(updated.CreatedBy, Is.EqualTo("tester"));
        Assert.That(updated.AssignedTo, Is.EqualTo("dev"));
        Assert.That(updated.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(updated.ParentIssues[0].ParentIssue, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task AddParentAsync_PreservesFieldsOnRetrieve()
    {
        var parent = await _service.CreateIssueAsync(ProjectPath, "Parent", IssueType.Feature);

        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "child-002",
            Title = "Child",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            Tags = ["preserved-tag"],
            LinkedPR = 99,
            CreatedBy = "author",
            AssignedTo = "assignee",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        await _service.AddParentAsync(ProjectPath, "child-002", parent.Id);

        var retrieved = await _service.GetIssueAsync(ProjectPath, "child-002");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Tags, Is.EqualTo(new[] { "preserved-tag" }));
        Assert.That(retrieved.LinkedPR, Is.EqualTo(99));
        Assert.That(retrieved.CreatedBy, Is.EqualTo("author"));
        Assert.That(retrieved.AssignedTo, Is.EqualTo("assignee"));
    }

    #endregion

    #region RemoveParent Preserves Fields

    [Test]
    public async Task RemoveParentAsync_PreservesAllFields()
    {
        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "child-003",
            Title = "Child",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            Priority = 1,
            Tags = ["important"],
            LinkedIssues = ["ref-1", "ref-2"],
            LinkedPR = 7,
            CreatedBy = "creator",
            AssignedTo = "worker",
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-x", SortOrder = "0" }],
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        var updated = await _service.RemoveParentAsync(ProjectPath, "child-003", "parent-x");

        Assert.That(updated.ParentIssues, Has.Count.EqualTo(0));
        Assert.That(updated.Tags, Is.EqualTo(new[] { "important" }));
        Assert.That(updated.LinkedIssues, Is.EqualTo(new[] { "ref-1", "ref-2" }));
        Assert.That(updated.LinkedPR, Is.EqualTo(7));
        Assert.That(updated.CreatedBy, Is.EqualTo("creator"));
        Assert.That(updated.AssignedTo, Is.EqualTo("worker"));
    }

    #endregion

    #region ListIssues Terminal Status Filtering

    [Test]
    public async Task ListIssuesAsync_ExcludesTerminalStatuses_WhenNoFiltersSpecified()
    {
        await _service.CreateIssueAsync(ProjectPath, "Open Issue", IssueType.Task, status: IssueStatus.Open);
        await _service.CreateIssueAsync(ProjectPath, "In Progress", IssueType.Task, status: IssueStatus.Progress);

        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "complete-1",
            Title = "Complete Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });
        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "archived-1",
            Title = "Archived Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Archived,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });
        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "closed-1",
            Title = "Closed Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Closed,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        var issues = await _service.ListIssuesAsync(ProjectPath);

        Assert.That(issues, Has.Count.EqualTo(2));
        Assert.That(issues.Select(i => i.Title), Is.EquivalentTo(new[] { "Open Issue", "In Progress" }));
    }

    [Test]
    public async Task ListIssuesAsync_WithStatusFilter_ReturnsMatchingTerminalStatus()
    {
        await _service.CreateIssueAsync(ProjectPath, "Open Issue", IssueType.Task, status: IssueStatus.Open);

        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "complete-2",
            Title = "Complete Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        var issues = await _service.ListIssuesAsync(ProjectPath, status: IssueStatus.Complete);

        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Title, Is.EqualTo("Complete Issue"));
    }

    [Test]
    public async Task ListIssuesAsync_WithTypeFilter_ExcludesTerminalStatuses()
    {
        await _service.CreateIssueAsync(ProjectPath, "Open Bug", IssueType.Bug, status: IssueStatus.Open);

        _service.SeedIssue(ProjectPath, new Issue
        {
            Id = "complete-bug",
            Title = "Complete Bug",
            Type = IssueType.Bug,
            Status = IssueStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        });

        // When filtering by type only (not status), terminal statuses should still be excluded
        var issues = await _service.ListIssuesAsync(ProjectPath, type: IssueType.Bug);

        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Title, Is.EqualTo("Open Bug"));
    }

    #endregion

    #region TaskGraph

    [Test]
    public async Task GetTaskGraphAsync_IncludesNewlyCreatedIssues()
    {
        await _service.CreateIssueAsync(ProjectPath, "Task 1", IssueType.Task, status: IssueStatus.Open);
        await _service.CreateIssueAsync(ProjectPath, "Task 2", IssueType.Task, status: IssueStatus.Open);

        var graph = await _service.GetTaskGraphAsync(ProjectPath);

        Assert.That(graph, Is.Not.Null);
    }

    #endregion

    #region Project Isolation

    [Test]
    public async Task Issues_AreIsolatedByProject()
    {
        const string project1 = "/project/one";
        const string project2 = "/project/two";

        await _service.CreateIssueAsync(project1, "Project 1 Issue", IssueType.Task);
        await _service.CreateIssueAsync(project2, "Project 2 Issue", IssueType.Bug);

        var issues1 = await _service.ListIssuesAsync(project1);
        var issues2 = await _service.ListIssuesAsync(project2);

        Assert.That(issues1, Has.Count.EqualTo(1));
        Assert.That(issues1[0].Title, Is.EqualTo("Project 1 Issue"));
        Assert.That(issues2, Has.Count.EqualTo(1));
        Assert.That(issues2[0].Title, Is.EqualTo("Project 2 Issue"));
    }

    [Test]
    public async Task GetIssueAsync_ReturnsNull_ForWrongProject()
    {
        var created = await _service.CreateIssueAsync("/project/a", "Issue A", IssueType.Task);

        var result = await _service.GetIssueAsync("/project/b", created.Id);

        Assert.That(result, Is.Null);
    }

    #endregion
}
