using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.AgentOrchestration.Controllers;

namespace Homespun.Api.Tests.Features.AgentOrchestration;

/// <summary>
/// Integration tests for <c>POST /api/orchestration/generate-branch-id</c>.
///
/// The mock-mode AppFactory does not configure a sidecar URL, so every request
/// hits the deterministic-slug fallback path inside <c>BranchIdGeneratorService</c>.
/// That makes this fixture the right place to verify the
/// <c>Warning: 199</c> header that signals a fallback to the client.
/// </summary>
[TestFixture]
public class OrchestrationApiTests
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
    public async Task GenerateBranchId_WhenSidecarUnavailable_ReturnsDeterministicSlugAndWarningHeader()
    {
        var request = new GenerateBranchIdRequest("Fix login button on mobile");

        using var response = await _client.PostAsJsonAsync(
            "/api/orchestration/generate-branch-id",
            request,
            JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<GenerateBranchIdResponse>(JsonOptions);
        Assert.That(body, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(body!.Success, Is.True);
            Assert.That(body.WasAiGenerated, Is.False, "expected fallback when no sidecar is configured");
            Assert.That(body.BranchId, Is.EqualTo("fix-login-button-on-mobile"));
        });

        var warningValues = response.Headers.TryGetValues("Warning", out var values)
            ? string.Join("|", values)
            : null;

        Assert.That(warningValues, Is.Not.Null,
            "expected Warning header so clients can flag the fallback path");
        Assert.That(warningValues, Does.Contain("199"));
        Assert.That(warningValues, Does.Contain("homespun"));
    }

    [Test]
    public async Task GenerateBranchId_BadRequest_DoesNotEmitWarningHeader()
    {
        var request = new GenerateBranchIdRequest("");

        using var response = await _client.PostAsJsonAsync(
            "/api/orchestration/generate-branch-id",
            request,
            JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Headers.Contains("Warning"), Is.False);
    }
}
