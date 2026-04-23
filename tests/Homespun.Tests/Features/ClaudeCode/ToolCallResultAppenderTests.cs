using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for <see cref="ToolCallResultAppender"/>: synthetic tool_result
/// A2A user-message construction and delegation to the ingestor.
/// </summary>
[TestFixture]
public class ToolCallResultAppenderTests
{
    private const string ProjectId = "proj-1";
    private const string SessionId = "sess-1";

    private Mock<ISessionEventIngestor> _ingestorMock = null!;
    private ToolCallResultAppender _appender = null!;

    [SetUp]
    public void SetUp()
    {
        _ingestorMock = new Mock<ISessionEventIngestor>();
        _appender = new ToolCallResultAppender(
            _ingestorMock.Object,
            new Mock<ILogger<ToolCallResultAppender>>().Object);
    }

    [Test]
    public async Task AppendAsync_NullToolCallId_IsNoop()
    {
        await _appender.AppendAsync(ProjectId, SessionId, toolCallId: null, resultPayload: new { ok = true });

        _ingestorMock.Verify(
            i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AppendAsync_EmptyToolCallId_IsNoop()
    {
        await _appender.AppendAsync(ProjectId, SessionId, toolCallId: string.Empty, resultPayload: new { ok = true });

        _ingestorMock.Verify(
            i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AppendAsync_WithPayload_EmitsToolResultMessageThroughIngestor()
    {
        JsonElement capturedPayload = default;
        string capturedKind = "";

        _ingestorMock
            .Setup(i => i.IngestAsync(
                ProjectId, SessionId, It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, JsonElement, CancellationToken>((_, _, kind, payload, _) =>
            {
                capturedKind = kind;
                capturedPayload = payload.Clone();
            })
            .Returns(Task.CompletedTask);

        await _appender.AppendAsync(
            ProjectId,
            SessionId,
            toolCallId: "tool-42",
            resultPayload: new Dictionary<string, string> { ["What is your name?"] = "Alice" });

        Assert.That(capturedKind, Is.EqualTo(HomespunA2AEventKind.Message));
        Assert.That(capturedPayload.GetProperty("kind").GetString(), Is.EqualTo("message"));
        Assert.That(capturedPayload.GetProperty("role").GetString(), Is.EqualTo("user"));

        var parts = capturedPayload.GetProperty("parts");
        Assert.That(parts.GetArrayLength(), Is.EqualTo(1));
        var part = parts[0];
        Assert.Multiple(() =>
        {
            Assert.That(part.GetProperty("kind").GetString(), Is.EqualTo("data"));
            Assert.That(part.GetProperty("metadata").GetProperty("kind").GetString(), Is.EqualTo("tool_result"));
            Assert.That(part.GetProperty("data").GetProperty("toolUseId").GetString(), Is.EqualTo("tool-42"));
        });

        // The content object must survive the round-trip as a JSON value — the translator's
        // TranslateUserMessageParts calls GetRawText() on it, so arbitrary JSON is accepted.
        var content = part.GetProperty("data").GetProperty("content");
        Assert.That(content.GetProperty("What is your name?").GetString(), Is.EqualTo("Alice"));
    }

    [Test]
    public async Task AppendAsync_ApprovalPayload_SerialisesApprovedKeepContextFeedback()
    {
        JsonElement capturedPayload = default;
        _ingestorMock
            .Setup(i => i.IngestAsync(ProjectId, SessionId, It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, JsonElement, CancellationToken>((_, _, _, payload, _) =>
            {
                capturedPayload = payload.Clone();
            })
            .Returns(Task.CompletedTask);

        await _appender.AppendAsync(
            ProjectId,
            SessionId,
            toolCallId: "plan-7",
            resultPayload: new { approved = true, keepContext = false, feedback = "lgtm" });

        var content = capturedPayload
            .GetProperty("parts")[0]
            .GetProperty("data")
            .GetProperty("content");

        Assert.Multiple(() =>
        {
            Assert.That(content.GetProperty("approved").GetBoolean(), Is.True);
            Assert.That(content.GetProperty("keepContext").GetBoolean(), Is.False);
            Assert.That(content.GetProperty("feedback").GetString(), Is.EqualTo("lgtm"));
        });
    }
}
