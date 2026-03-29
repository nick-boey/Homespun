using System.Net;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class HealthCheckApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

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
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AliveEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/alive");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
