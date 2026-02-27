using System.Net;
using System.Net.Http.Json;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests for the Issues API endpoints.
/// </summary>
[TestFixture]
public class IssuesApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetResolvedBranch_ReturnsNotFound_WhenProjectNotExists()
    {
        // Act
        var response = await _client.GetAsync("/api/issues/issue-123/resolved-branch?projectId=nonexistent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetResolvedBranch_ReturnsNullBranch_WhenNoMatchFound()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        // Act
        var response = await _client.GetAsync("/api/issues/issue-123/resolved-branch?projectId=proj1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ResolvedBranchResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.BranchName, Is.Null);
    }

    [Test]
    public async Task GetResolvedBranch_ReturnsLinkedPRBranch_WhenPRExists()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        var pr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = "proj1",
            Title = "Test PR",
            BranchName = "feature/existing-work+issue-123",
            BeadsIssueId = "issue-123"
        };
        _factory.MockDataStore.SeedProject(project);
        _factory.MockDataStore.SeedPullRequest(pr);

        // Act
        var response = await _client.GetAsync("/api/issues/issue-123/resolved-branch?projectId=proj1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ResolvedBranchResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.BranchName, Is.EqualTo("feature/existing-work+issue-123"));
    }

    [Test]
    public async Task GetResolvedBranch_ReturnsNullBranch_WhenPRHasDifferentIssueId()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        var pr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = "proj1",
            Title = "Test PR",
            BranchName = "feature/other-work+other-issue",
            BeadsIssueId = "other-issue" // Different issue ID
        };
        _factory.MockDataStore.SeedProject(project);
        _factory.MockDataStore.SeedPullRequest(pr);

        // Act
        var response = await _client.GetAsync("/api/issues/issue-123/resolved-branch?projectId=proj1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ResolvedBranchResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.BranchName, Is.Null);
    }

    [Test]
    public async Task GetResolvedBranch_ReturnsNullBranch_WhenPRHasNoBranchName()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/path", DefaultBranch = "main" };
        var pr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = "proj1",
            Title = "Test PR",
            BranchName = null, // No branch name
            BeadsIssueId = "issue-123"
        };
        _factory.MockDataStore.SeedProject(project);
        _factory.MockDataStore.SeedPullRequest(pr);

        // Act - the resolver should fall back to clone search, but since there are no clones, it returns null
        var response = await _client.GetAsync("/api/issues/issue-123/resolved-branch?projectId=proj1");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ResolvedBranchResponse>();
        Assert.That(result, Is.Not.Null);
        // No matching clone either, so null is returned
        Assert.That(result!.BranchName, Is.Null);
    }

    #region SetParent Tests

    [Test]
    public async Task SetParent_ReturnsNotFound_WhenProjectNotExists()
    {
        // Arrange
        var request = new SetParentRequest
        {
            ProjectId = "nonexistent",
            ParentIssueId = "parent-123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/child-456/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetParent_ReturnsNotFound_WhenChildIssueNotExists()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, new Issue
        {
            Id = "parent-123",
            Title = "Parent Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        });

        var request = new SetParentRequest
        {
            ProjectId = "proj1",
            ParentIssueId = "parent-123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/nonexistent/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetParent_ReturnsBadRequest_WhenCycleDetected()
    {
        // Arrange - create A -> B hierarchy where B is child of A
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var issueA = new Issue
        {
            Id = "issue-A",
            Title = "Issue A",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var issueB = new Issue
        {
            Id = "issue-B",
            Title = "Issue B",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "issue-A", SortOrder = "0" }]
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, issueA);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, issueB);

        // Try to make A a child of B (would create cycle)
        var request = new SetParentRequest
        {
            ProjectId = "proj1",
            ParentIssueId = "issue-B"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/issue-A/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SetParent_ReturnsOk_WhenValidRequest()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var parentIssue = new Issue
        {
            Id = "parent-123",
            Title = "Parent Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var childIssue = new Issue
        {
            Id = "child-456",
            Title = "Child Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, parentIssue);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, childIssue);

        var request = new SetParentRequest
        {
            ProjectId = "proj1",
            ParentIssueId = "parent-123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/child-456/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(result.ParentIssues[0].ParentIssue, Is.EqualTo("parent-123"));
    }

    [Test]
    public async Task SetParent_AddsToExisting_WhenAddToExistingTrue()
    {
        // Arrange - child already has parentA, we want to add parentB
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var parentA = new Issue
        {
            Id = "parent-A",
            Title = "Parent A",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var parentB = new Issue
        {
            Id = "parent-B",
            Title = "Parent B",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var childIssue = new Issue
        {
            Id = "child-456",
            Title = "Child Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-A", SortOrder = "0" }]
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, parentA);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, parentB);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, childIssue);

        var request = new SetParentRequest
        {
            ProjectId = "proj1",
            ParentIssueId = "parent-B",
            AddToExisting = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/child-456/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ParentIssues, Has.Count.EqualTo(2));
        var parentIds = result.ParentIssues.Select(p => p.ParentIssue).ToList();
        Assert.That(parentIds, Does.Contain("parent-A"));
        Assert.That(parentIds, Does.Contain("parent-B"));
    }

    [Test]
    public async Task SetParent_ReplacesExisting_WhenAddToExistingFalse()
    {
        // Arrange - child already has parentA, we want to replace with parentB
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var parentA = new Issue
        {
            Id = "parent-A",
            Title = "Parent A",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var parentB = new Issue
        {
            Id = "parent-B",
            Title = "Parent B",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var childIssue = new Issue
        {
            Id = "child-456",
            Title = "Child Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-A", SortOrder = "0" }]
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, parentA);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, parentB);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, childIssue);

        var request = new SetParentRequest
        {
            ProjectId = "proj1",
            ParentIssueId = "parent-B",
            AddToExisting = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/child-456/set-parent", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ParentIssues, Has.Count.EqualTo(1));
        Assert.That(result.ParentIssues[0].ParentIssue, Is.EqualTo("parent-B"));
    }

    #endregion

    #region MoveSeriesSibling Tests

    [Test]
    public async Task MoveSeriesSibling_ReturnsNotFound_WhenProjectNotExists()
    {
        // Arrange
        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "nonexistent",
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/issue-123/move-sibling", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task MoveSeriesSibling_ReturnsNotFound_WhenIssueNotExists()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "proj1",
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/nonexistent/move-sibling", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task MoveSeriesSibling_ReturnsBadRequest_WhenNoParent()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var issue = new Issue
        {
            Id = "issue-123",
            Title = "Test Issue",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [] // No parent
        };
        _factory.MockFleeceService.SeedIssue(project.LocalPath, issue);

        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "proj1",
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/issue-123/move-sibling", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task MoveSeriesSibling_ReturnsOk_WhenValidMoveUp()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var parent = new Issue
        {
            Id = "parent-1",
            Title = "Parent Issue",
            Type = IssueType.Feature,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var child1 = new Issue
        {
            Id = "child-1",
            Title = "Child 1",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-1", SortOrder = "0" }]
        };
        var child2 = new Issue
        {
            Id = "child-2",
            Title = "Child 2",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-1", SortOrder = "1" }]
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, parent);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, child1);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, child2);

        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "proj1",
            Direction = MoveDirection.Up
        };

        // Act - move child2 up
        var response = await _client.PostAsJsonAsync("/api/issues/child-2/move-sibling", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("child-2"));
    }

    [Test]
    public async Task MoveSeriesSibling_ReturnsOk_WhenValidMoveDown()
    {
        // Arrange
        var project = new Project { Id = "proj1", Name = "TestProject", LocalPath = "/tmp/test-project", DefaultBranch = "main" };
        _factory.MockDataStore.SeedProject(project);

        var parent = new Issue
        {
            Id = "parent-1",
            Title = "Parent Issue",
            Type = IssueType.Feature,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow
        };
        var child1 = new Issue
        {
            Id = "child-1",
            Title = "Child 1",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-1", SortOrder = "0" }]
        };
        var child2 = new Issue
        {
            Id = "child-2",
            Title = "Child 2",
            Type = IssueType.Task,
            Status = IssueStatus.Open,
            LastUpdate = DateTime.UtcNow,
            ParentIssues = [new ParentIssueRef { ParentIssue = "parent-1", SortOrder = "1" }]
        };

        _factory.MockFleeceService.SeedIssue(project.LocalPath, parent);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, child1);
        _factory.MockFleeceService.SeedIssue(project.LocalPath, child2);

        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "proj1",
            Direction = MoveDirection.Down
        };

        // Act - move child1 down
        var response = await _client.PostAsJsonAsync("/api/issues/child-1/move-sibling", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("child-1"));
    }

    #endregion
}
