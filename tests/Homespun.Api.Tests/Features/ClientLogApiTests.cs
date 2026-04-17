using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homespun.Features.Observability;

namespace Homespun.Api.Tests.Features;

[TestFixture]
[NonParallelizable]
public class ClientLogApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private StringWriter _consoleOutput = null!;
    private TextWriter _originalOutput = null!;

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

    [SetUp]
    public void SetUp()
    {
        _originalOutput = Console.Out;
        _consoleOutput = new StringWriter();
        Console.SetOut(_consoleOutput);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(_originalOutput);
        _consoleOutput.Dispose();
    }

    private static ClientLogEntry MakeEntry(
        string hop = "client.reducer.apply",
        string sessionId = "S1",
        string? messageId = "M1",
        string level = "Information") => new()
    {
        Timestamp = "2026-04-17T10:00:00.000Z",
        Level = level,
        Message = $"{hop} seq=1 msg={messageId}",
        Hop = hop,
        SessionId = sessionId,
        MessageId = messageId,
        EventId = "e1",
        Seq = 1,
        AGUIType = "TEXT_MESSAGE_CONTENT",
    };

    [Test]
    public async Task Post_HappyPath_Returns202AndForwardsEntries()
    {
        var batch = new[] { MakeEntry(), MakeEntry(hop: "client.signalr.rx") };

        var response = await _client.PostAsJsonAsync("/api/log/client", batch, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // Two entries should have been forwarded as stdout JSON.
        var lines = _consoleOutput.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("Homespun.ClientSessionEvents"))
            .ToList();
        Assert.That(lines, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Post_OversizedBatch_Returns413()
    {
        var batch = Enumerable.Range(0, ClientLogController.MaxBatchSize + 1)
            .Select(_ => MakeEntry())
            .ToArray();

        var response = await _client.PostAsJsonAsync("/api/log/client", batch, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [Test]
    public async Task Post_MalformedEntry_Returns400()
    {
        // Post an entry missing a required field (SessionId) — model binding or
        // controller validation both return 400; either is an acceptable
        // malformed-batch response.
        var batch = new[]
        {
            MakeEntry(),
            MakeEntry() with { SessionId = "" },
        };

        var response = await _client.PostAsJsonAsync("/api/log/client", batch, JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Post_ClientReportsWarnLevel_PreservedInForward()
    {
        var batch = new[] { MakeEntry(level: "Warning") };
        await _client.PostAsJsonAsync("/api/log/client", batch, JsonOptions);

        var line = _consoleOutput.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.Contains("Homespun.ClientSessionEvents"));
        using var doc = JsonDocument.Parse(line);
        Assert.That(doc.RootElement.GetProperty("Level").GetString(), Is.EqualTo("Warning"));
    }

    [Test]
    public async Task Post_ForcesSourceContextToClientSessionEvents()
    {
        var batch = new[] { MakeEntry() };
        await _client.PostAsJsonAsync("/api/log/client", batch, JsonOptions);

        var line = _consoleOutput.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.Contains("Homespun.ClientSessionEvents"));
        using var doc = JsonDocument.Parse(line);
        Assert.That(doc.RootElement.GetProperty("SourceContext").GetString(),
            Is.EqualTo("Homespun.ClientSessionEvents"));
    }
}
