using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for the SDK MessageParser class.
/// </summary>
[TestFixture]
public class MessageParserTests
{
    [Test]
    public void ParseMessage_RateLimitEvent_ReturnsRateLimitEventType()
    {
        var data = CreateMessageData("rate_limit_event");

        var result = MessageParser.ParseMessage(data);

        Assert.That(result, Is.InstanceOf<RateLimitEvent>());
    }

    [Test]
    public void ParseMessage_RateLimitEvent_WithData_ParsesData()
    {
        var data = CreateMessageData("rate_limit_event", new Dictionary<string, object>
        {
            ["retry_after_ms"] = 5000,
            ["reason"] = "rate_limited"
        });

        var result = MessageParser.ParseMessage(data);

        Assert.That(result, Is.InstanceOf<RateLimitEvent>());
        var rateLimitEvent = (RateLimitEvent)result;
        Assert.That(rateLimitEvent.Data, Is.Not.Null);
        Assert.That(rateLimitEvent.Data!.ContainsKey("retry_after_ms"), Is.True);
        Assert.That(rateLimitEvent.Data!.ContainsKey("reason"), Is.True);
    }

    [Test]
    public void ParseMessage_RateLimitEvent_WithoutData_ReturnsNullData()
    {
        var data = CreateMessageData("rate_limit_event");

        var result = MessageParser.ParseMessage(data);

        Assert.That(result, Is.InstanceOf<RateLimitEvent>());
        var rateLimitEvent = (RateLimitEvent)result;
        Assert.That(rateLimitEvent.Data, Is.Null);
    }

    [Test]
    public void ParseMessage_UnknownType_ThrowsArgumentException()
    {
        var data = CreateMessageData("unknown_type_xyz");

        var ex = Assert.Throws<ArgumentException>(() => MessageParser.ParseMessage(data));
        Assert.That(ex!.Message, Does.Contain("Unknown message type: unknown_type_xyz"));
    }

    [Test]
    public void ParseMessage_ControlRequest_ReturnsControlRequestType()
    {
        var data = CreateMessageData("control_request");

        var result = MessageParser.ParseMessage(data);

        Assert.That(result, Is.InstanceOf<ControlRequest>());
    }

    private static Dictionary<string, object> CreateMessageData(string type, Dictionary<string, object>? additionalData = null)
    {
        var json = new Dictionary<string, object>
        {
            ["type"] = type
        };

        if (additionalData != null)
        {
            json["data"] = additionalData;
        }

        // Serialize and deserialize to get JsonElement values (matching real SDK behavior)
        var jsonString = JsonSerializer.Serialize(json);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString)!;
    }
}
