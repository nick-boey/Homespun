using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class ModelsApiTests
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
    public async Task Get_api_models_returns_catalog_with_exactly_one_default()
    {
        var response = await _client.GetAsync("/api/models");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var catalog = await response.Content.ReadFromJsonAsync<List<ClaudeModelInfo>>(JsonOptions);

        Assert.That(catalog, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(catalog!, Is.Not.Empty, "Catalog must return at least the fallback models in mock mode.");
            Assert.That(catalog.Count(m => m.IsDefault), Is.EqualTo(1), "Exactly one model must be marked IsDefault.");
        });
    }
}
