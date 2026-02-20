using System.Net;
using System.Net.Http.Json;
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
}
