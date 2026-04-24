using System.Net;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Unit tests for <see cref="PerSessionEventStream"/> — the long-lived worker SSE
/// consumer that drives <see cref="ISessionEventIngestor"/> on every A2A event and
/// fans parsed <see cref="SdkMessage"/> values out to per-turn subscribers.
/// </summary>
[TestFixture]
public class PerSessionEventStreamTests
{
    private const string HomespunSessionId = "s1";
    private const string WorkerSessionId = "w1";
    private const string ProjectId = "p1";
    private const string WorkerBaseUrl = "http://fake";

    /// <summary>
    /// Fakes the worker's <c>GET /api/sessions/{id}/events</c> response by emitting a
    /// pre-baked SSE body once, then returning EOF. The stream ends as soon as the
    /// reader's <c>StreamReader</c> exhausts the buffer — plenty of time for the
    /// background reader to dispatch every frame.
    /// </summary>
    private sealed class SseResponseHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly List<HttpRequestMessage> _requests = new();

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        public SseResponseHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Builds an SSE body with two A2A frames: a <c>status-update</c> carrying a
    /// <c>completed</c> state + <c>final: true</c>, and a <c>message</c> frame shaped
    /// as a minimal agent message. The body is terminated by a trailing blank line so
    /// the reader flushes the second frame before EOF.
    /// </summary>
    private static string BuildTwoFrameSseBody()
    {
        var statusUpdate = JsonSerializer.Serialize(new
        {
            kind = "status-update",
            taskId = "t1",
            contextId = HomespunSessionId,
            status = new
            {
                state = "completed",
                timestamp = "2026-04-24T00:00:00Z",
            },
            final = true,
        });

        var message = JsonSerializer.Serialize(new
        {
            kind = "message",
            messageId = "m1",
            role = "agent",
            taskId = "t1",
            contextId = HomespunSessionId,
            parts = new object[]
            {
                new { kind = "text", text = "hi" },
            },
            metadata = new
            {
                sdkMessageType = "task_notification",
            },
        });

        var sb = new StringBuilder();
        sb.Append("event: status-update\n");
        sb.Append("data: ").Append(statusUpdate).Append('\n');
        sb.Append('\n');
        sb.Append("event: message\n");
        sb.Append("data: ").Append(message).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    private static async Task WaitForConditionAsync(
        Func<bool> predicate,
        TimeSpan? timeout = null,
        TimeSpan? poll = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        var delay = poll ?? TimeSpan.FromMilliseconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(delay);
        }
    }

    [Test]
    public async Task StartAsync_DispatchesEveryA2AFrameToIngestor()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        ingestor
            .Setup(i => i.IngestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var handler = new SseResponseHandler(BuildTwoFrameSseBody());
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);

        // Wait until both frames have been dispatched (or timeout).
        await WaitForConditionAsync(() =>
        {
            try
            {
                ingestor.Verify(i => i.IngestAsync(
                    ProjectId, HomespunSessionId, "status-update",
                    It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
                ingestor.Verify(i => i.IngestAsync(
                    ProjectId, HomespunSessionId, "message",
                    It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch (MockException)
            {
                return false;
            }
        });

        ingestor.Verify(i => i.IngestAsync(
            ProjectId, HomespunSessionId, "status-update",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        ingestor.Verify(i => i.IngestAsync(
            ProjectId, HomespunSessionId, "message",
            It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);

        // Request was shaped like the worker contract.
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var req = handler.Requests[0];
        Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(req.RequestUri?.ToString(), Is.EqualTo($"{WorkerBaseUrl}/api/sessions/{WorkerSessionId}/events"));
        Assert.That(req.Headers.Accept.ToString(), Does.Contain("text/event-stream"));

        await stream.StopAsync(HomespunSessionId);
        await stream.DisposeAsync();
    }

    [Test]
    public async Task StartAsync_IsIdempotentForSameSession()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        ingestor
            .Setup(i => i.IngestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var handler = new SseResponseHandler(BuildTwoFrameSseBody());
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);
        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);

        // Give the reader a moment to settle; only one HTTP request should have been made.
        await Task.Delay(200);

        Assert.That(handler.Requests, Has.Count.EqualTo(1),
            "second StartAsync for the same session must be a no-op");

        await stream.DisposeAsync();
    }

    [Test]
    public async Task SubscribeTurnAsync_YieldsSdkResultMessageThenCompletes()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        ingestor
            .Setup(i => i.IngestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var handler = new SseResponseHandler(BuildTwoFrameSseBody());
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var messages = new List<SdkMessage>();
        await foreach (var msg in stream.SubscribeTurnAsync(HomespunSessionId, cts.Token))
        {
            messages.Add(msg);
            if (msg is SdkResultMessage) break;
        }

        Assert.That(messages, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(messages.OfType<SdkResultMessage>().ToList(), Has.Count.EqualTo(1),
            "status-update completed must materialize as exactly one SdkResultMessage");

        await stream.DisposeAsync();
    }

    [Test]
    public void SubscribeTurnAsync_ThrowsWhenReaderNotStarted()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        using var handler = new SseResponseHandler(string.Empty);
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in stream.SubscribeTurnAsync("never-started", CancellationToken.None))
            {
                // never reached
            }
        });
    }

    [Test]
    public async Task StopAsync_IsSafeForUnknownSession()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        using var handler = new SseResponseHandler(string.Empty);
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        Assert.DoesNotThrowAsync(async () => await stream.StopAsync("unknown"));

        await stream.DisposeAsync();
    }
}
