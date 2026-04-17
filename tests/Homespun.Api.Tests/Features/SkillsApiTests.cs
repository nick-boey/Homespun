using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class SkillsApiTests
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
    public async Task GetForProject_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.GetAsync("/api/skills/project/nonexistent-id");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetForProject_ReturnsEmptyGroups_WhenProjectHasNoSkillsDirectory()
    {
        // Arrange - create a project whose LocalPath will not contain .claude/skills/
        var createRequest = new { Name = "SkillsApiTest", DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", createRequest, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/skills/project/{project!.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var skills = await response.Content.ReadFromJsonAsync<DiscoveredSkills>(JsonOptions);
        Assert.That(skills, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(skills!.OpenSpec, Is.Empty);
            Assert.That(skills.Homespun, Is.Empty);
            Assert.That(skills.General, Is.Empty);
        });
    }
}
