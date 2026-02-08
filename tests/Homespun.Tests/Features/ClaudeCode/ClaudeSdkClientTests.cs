using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.ClaudeAgentSdk.Transport;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ClaudeSdkClientTests
{
    private Mock<ITransport> _transportMock = null!;
    private ClaudeSdkClient _client = null!;
    private string? _capturedJson;

    [SetUp]
    public async Task SetUp()
    {
        _capturedJson = null;
        _transportMock = new Mock<ITransport>();
        _transportMock.Setup(t => t.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _transportMock.Setup(t => t.WriteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((json, _) => _capturedJson = json)
            .Returns(Task.CompletedTask);
        _transportMock.SetupGet(t => t.IsReady).Returns(true);

        _client = new ClaudeSdkClient(transport: _transportMock.Object);
        await _client.ConnectAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _client.DisposeAsync();
    }

    [Test]
    public async Task SendControlResponseAsync_Allow_WritesCorrectNestedJson()
    {
        // Act
        await _client.SendControlResponseAsync("req-123", "allow");

        // Assert
        Assert.That(_capturedJson, Is.Not.Null);
        var parsed = JsonSerializer.Deserialize<JsonElement>(_capturedJson!.TrimEnd('\n'));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-123"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("allow"));
            Assert.That(innerResponse.TryGetProperty("updatedInput", out _), Is.True,
                "Allow responses must always include updatedInput");
        });
    }

    [Test]
    public async Task SendControlResponseAsync_AllowWithoutUpdatedInput_IncludesEmptyUpdatedInput()
    {
        // Act - call without providing updatedInput
        await _client.SendControlResponseAsync("req-123", "allow");

        // Assert
        Assert.That(_capturedJson, Is.Not.Null);
        var parsed = JsonSerializer.Deserialize<JsonElement>(_capturedJson!.TrimEnd('\n'));

        var innerResponse = parsed.GetProperty("response").GetProperty("response");
        Assert.That(innerResponse.TryGetProperty("updatedInput", out var updatedInput), Is.True,
            "Allow responses must always include updatedInput, even when not explicitly provided");
        Assert.That(updatedInput.ValueKind, Is.EqualTo(JsonValueKind.Object),
            "Default updatedInput should be an empty object");
        Assert.That(updatedInput.EnumerateObject().Count(), Is.EqualTo(0),
            "Default updatedInput should have no properties");
    }

    [Test]
    public async Task SendControlResponseAsync_Deny_DoesNotIncludeUpdatedInput()
    {
        // Act
        await _client.SendControlResponseAsync("req-789", "deny", denyMessage: "Denied");

        // Assert
        Assert.That(_capturedJson, Is.Not.Null);
        var parsed = JsonSerializer.Deserialize<JsonElement>(_capturedJson!.TrimEnd('\n'));

        var innerResponse = parsed.GetProperty("response").GetProperty("response");
        Assert.That(innerResponse.TryGetProperty("updatedInput", out _), Is.False,
            "Deny responses should NOT include updatedInput");
    }

    [Test]
    public async Task SendControlResponseAsync_AllowWithUpdatedInput_WritesCorrectNestedJson()
    {
        // Arrange
        var updatedInput = new Dictionary<string, object>
        {
            ["answers"] = new Dictionary<string, string>
            {
                ["Which database?"] = "PostgreSQL"
            }
        };

        // Act
        await _client.SendControlResponseAsync("req-456", "allow", updatedInput);

        // Assert
        Assert.That(_capturedJson, Is.Not.Null);
        var parsed = JsonSerializer.Deserialize<JsonElement>(_capturedJson!.TrimEnd('\n'));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-456"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("allow"));
            Assert.That(innerResponse.TryGetProperty("updatedInput", out _), Is.True);
        });
    }

    [Test]
    public async Task SendControlResponseAsync_Deny_WritesCorrectNestedJson()
    {
        // Act
        await _client.SendControlResponseAsync("req-789", "deny", denyMessage: "User denied this action");

        // Assert
        Assert.That(_capturedJson, Is.Not.Null);
        var parsed = JsonSerializer.Deserialize<JsonElement>(_capturedJson!.TrimEnd('\n'));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.GetProperty("type").GetString(), Is.EqualTo("control_response"));
            var response = parsed.GetProperty("response");
            Assert.That(response.GetProperty("subtype").GetString(), Is.EqualTo("success"));
            Assert.That(response.GetProperty("request_id").GetString(), Is.EqualTo("req-789"));
            var innerResponse = response.GetProperty("response");
            Assert.That(innerResponse.GetProperty("behavior").GetString(), Is.EqualTo("deny"));
            Assert.That(innerResponse.GetProperty("message").GetString(), Is.EqualTo("User denied this action"));
        });
    }

    [Test]
    public void SendControlResponseAsync_NotConnected_Throws()
    {
        // Arrange - create a new client without connecting
        var client = new ClaudeSdkClient(transport: _transportMock.Object);

        // Act & Assert
        Assert.ThrowsAsync<CliConnectionException>(async () =>
            await client.SendControlResponseAsync("req-123", "allow"));
    }
}
