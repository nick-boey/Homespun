using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.OpenSpec;

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
}
