using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.OpenSpec;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.OpenSpec;

[TestFixture]
public class ChangeSnapshotApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task PostBranchState_ThenGet_ReturnsStoredSnapshot()
    {
        var branch = $"feat/test+{Guid.NewGuid():N}"[..20];
        var request = new BranchStateRequest
        {
            ProjectId = "proj-api-1",
            Branch = branch,
            FleeceId = "issue-xyz",
            Changes = new List<SnapshotChange>
            {
                new()
                {
                    Name = "my-change",
                    CreatedBy = "agent",
                    IsArchived = false,
                    TasksDone = 3,
                    TasksTotal = 7,
                    NextIncomplete = "Build scanner"
                }
            },
            Orphans = new List<SnapshotOrphan>
            {
                new() { Name = "floating", CreatedOnBranch = true }
            }
        };

        var postResponse = await _client.PostAsJsonAsync("/api/openspec/branch-state", request, JsonOptions);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var posted = await postResponse.Content.ReadFromJsonAsync<BranchStateSnapshot>(JsonOptions);
        Assert.That(posted, Is.Not.Null);
        Assert.That(posted!.Changes, Has.Count.EqualTo(1));

        var getResponse = await _client.GetAsync(
            $"/api/openspec/branch-state?projectId=proj-api-1&branch={Uri.EscapeDataString(branch)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var retrieved = await getResponse.Content.ReadFromJsonAsync<BranchStateSnapshot>(JsonOptions);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.ProjectId, Is.EqualTo("proj-api-1"));
        Assert.That(retrieved.Branch, Is.EqualTo(branch));
        Assert.That(retrieved.FleeceId, Is.EqualTo("issue-xyz"));
        Assert.That(retrieved.Changes, Has.Count.EqualTo(1));
        Assert.That(retrieved.Changes[0].Name, Is.EqualTo("my-change"));
        Assert.That(retrieved.Changes[0].TasksDone, Is.EqualTo(3));
        Assert.That(retrieved.Orphans, Has.Count.EqualTo(1));
        Assert.That(retrieved.Orphans[0].Name, Is.EqualTo("floating"));
    }

    [Test]
    public async Task PostBranchState_Missing_Required_Returns400()
    {
        var bad = new BranchStateRequest
        {
            ProjectId = "",
            Branch = "",
            FleeceId = ""
        };

        var response = await _client.PostAsJsonAsync("/api/openspec/branch-state", bad, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetBranchState_UnknownBranch_Returns404()
    {
        var response = await _client.GetAsync(
            "/api/openspec/branch-state?projectId=proj-api-1&branch=never-seen");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task LinkOrphan_Branchless_WritesSidecarAcrossEveryCloneCarryingChange()
    {
        var (projectId, mainPath, cloneRoot) = await SetupProjectWithChangeAsync(
            changeName: "branchless-multi",
            cloneBranches: ["feat/a+ISSUE-AAA", "feat/b+ISSUE-BBB"]);

        var body = new
        {
            projectId,
            changeName = "branchless-multi",
            fleeceId = "ISSUE-LINK-1"
        };

        var response = await _client.PostAsJsonAsync("/api/openspec/changes/link", body, JsonOptions);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var mainSidecar = Path.Combine(mainPath, "openspec", "changes", "branchless-multi", ".homespun.yaml");
        Assert.That(File.Exists(mainSidecar), Is.True, "main clone should have a sidecar");

        var cloneA = Path.Combine(cloneRoot, "feat-a+ISSUE-AAA", "openspec", "changes", "branchless-multi", ".homespun.yaml");
        var cloneB = Path.Combine(cloneRoot, "feat-b+ISSUE-BBB", "openspec", "changes", "branchless-multi", ".homespun.yaml");
        Assert.That(File.Exists(cloneA), Is.True, "branch clone A should have a sidecar");
        Assert.That(File.Exists(cloneB), Is.True, "branch clone B should have a sidecar");

        var mainContent = await File.ReadAllTextAsync(mainSidecar);
        Assert.That(mainContent, Does.Contain("ISSUE-LINK-1"));
        Assert.That(mainContent, Does.Contain("server"));
    }

    [Test]
    public async Task LinkOrphan_Branchless_NoCloneCarriesChange_Returns404()
    {
        var (projectId, _, _) = await SetupProjectWithChangeAsync(
            changeName: "exists-on-clone",
            cloneBranches: ["feat/c+ISSUE-CCC"],
            seedMain: false);

        var body = new
        {
            projectId,
            changeName = "does-not-exist-anywhere",
            fleeceId = "ISSUE-LINK-2"
        };

        var response = await _client.PostAsJsonAsync("/api/openspec/changes/link", body, JsonOptions);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task LinkOrphan_BranchScoped_WritesToNamedCloneOnly()
    {
        var (projectId, mainPath, cloneRoot) = await SetupProjectWithChangeAsync(
            changeName: "branch-scoped-test",
            cloneBranches: ["feat/d+ISSUE-DDD", "feat/e+ISSUE-EEE"]);

        var body = new
        {
            projectId,
            branch = "feat/d+ISSUE-DDD",
            changeName = "branch-scoped-test",
            fleeceId = "ISSUE-LINK-3"
        };

        var response = await _client.PostAsJsonAsync("/api/openspec/changes/link", body, JsonOptions);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var cloneD = Path.Combine(cloneRoot, "feat-d+ISSUE-DDD", "openspec", "changes", "branch-scoped-test", ".homespun.yaml");
        Assert.That(File.Exists(cloneD), Is.True, "named clone should have a sidecar");

        var mainSidecar = Path.Combine(mainPath, "openspec", "changes", "branch-scoped-test", ".homespun.yaml");
        Assert.That(File.Exists(mainSidecar), Is.False, "main clone should NOT have a sidecar (branch-scoped form)");

        var cloneE = Path.Combine(cloneRoot, "feat-e+ISSUE-EEE", "openspec", "changes", "branch-scoped-test", ".homespun.yaml");
        Assert.That(File.Exists(cloneE), Is.False, "other clone should NOT have a sidecar (branch-scoped form)");
    }

    /// <summary>
    /// Materialises a project with the given change directory present on the main clone
    /// (when <paramref name="seedMain"/> is true) and on every named branch clone, then
    /// registers the project with <see cref="IDataStore"/> and the clones with
    /// <see cref="IGitCloneService"/> so the controller can discover them.
    /// </summary>
    private async Task<(string ProjectId, string MainPath, string CloneRoot)> SetupProjectWithChangeAsync(
        string changeName,
        IReadOnlyList<string> cloneBranches,
        bool seedMain = true)
    {
        var projectId = $"proj-{Guid.NewGuid():N}"[..16];
        var rootDir = Path.Combine(Path.GetTempPath(), "homespun-link-tests", projectId);
        var mainPath = Path.Combine(rootDir, "main");
        var cloneRoot = $"{mainPath}-clones";

        Directory.CreateDirectory(mainPath);
        if (seedMain)
        {
            Directory.CreateDirectory(Path.Combine(mainPath, "openspec", "changes", changeName));
        }

        var dataStore = _factory.Services.GetRequiredService<IDataStore>();
        await dataStore.AddProjectAsync(new Project
        {
            Id = projectId,
            Name = $"link-test-{projectId}",
            LocalPath = mainPath,
            DefaultBranch = "main"
        });

        var cloneService = _factory.Services.GetRequiredService<IGitCloneService>();
        foreach (var branch in cloneBranches)
        {
            var clonePath = await cloneService.CreateCloneAsync(mainPath, branch);
            Assert.That(clonePath, Is.Not.Null, $"clone for {branch} should be created");
            Directory.CreateDirectory(Path.Combine(clonePath!, "openspec", "changes", changeName));
        }

        return (projectId, mainPath, cloneRoot);
    }
}
