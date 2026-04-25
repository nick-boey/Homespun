using System.Net;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// The reader dispatches messages whether or not a subscriber is attached. This test
    /// proves the pre-subscribe buffer: seed the handler with a status-update completed
    /// (→ <see cref="SdkResultMessage"/>) AND a <c>question_pending</c> frame
    /// (→ <see cref="SdkQuestionPendingMessage"/>), let the reader drain them before any
    /// subscriber attaches, THEN call <see cref="IPerSessionEventStream.SubscribeTurnAsync"/>
    /// and assert both messages are replayed in order.
    /// </summary>
    [Test]
    public async Task BeginTurn_DrainsPreSubscribeBuffer_IntoNewChannel()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        ingestor
            .Setup(i => i.IngestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Build a body containing a question_pending frame FIRST, then a status-update
        // completed (→ SdkResultMessage). FIFO order must be preserved by the drain.
        var questionData = JsonSerializer.Serialize(new
        {
            questionId = "q1",
            prompt = "may I?",
        });
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

        var sb = new StringBuilder();
        sb.Append("event: question_pending\n");
        sb.Append("data: ").Append(questionData).Append('\n');
        sb.Append('\n');
        sb.Append("event: status-update\n");
        sb.Append("data: ").Append(statusUpdate).Append('\n');
        sb.Append('\n');

        using var handler = new SseResponseHandler(sb.ToString());
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(
            ingestor.Object, httpClient, NullLogger<PerSessionEventStream>.Instance);

        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);

        // Wait until the reader has drained the body into the pre-subscribe buffer. The
        // ingestor only sees A2A frames (status-update), so a single IngestAsync call is
        // our signal that the reader has processed both frames (question_pending is
        // dispatched in-order ahead of it, buffered synchronously under the lock).
        await WaitForConditionAsync(() =>
        {
            try
            {
                ingestor.Verify(i => i.IngestAsync(
                    ProjectId, HomespunSessionId, "status-update",
                    It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch (MockException)
            {
                return false;
            }
        });

        // Small yield so the reader has definitely enqueued the SdkResultMessage after
        // the ingestor call (both happen inside the same DispatchAsync call for the
        // status-update, but the SdkResultMessage push is after the ingestor await).
        await Task.Delay(50);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var messages = new List<SdkMessage>();
        await foreach (var msg in stream.SubscribeTurnAsync(HomespunSessionId, cts.Token))
        {
            messages.Add(msg);
            if (msg is SdkResultMessage) break;
        }

        Assert.That(messages, Has.Count.EqualTo(2),
            "buffer must replay both pre-subscribe messages into the new turn channel");
        Assert.That(messages[0], Is.InstanceOf<SdkQuestionPendingMessage>(),
            "FIFO order: question_pending was dispatched first");
        Assert.That(messages[1], Is.InstanceOf<SdkResultMessage>(),
            "FIFO order: status-update completed was dispatched second");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Seeds the handler with more than <c>PreSubscribeBufferCapacity</c> control frames
    /// (each <c>question_pending</c> → <see cref="SdkQuestionPendingMessage"/>). The
    /// reader drains them into the buffer with no subscriber attached, which must
    /// trigger oldest-first eviction and log a Warning on each overflow. On attach the
    /// subscriber sees exactly <c>PreSubscribeBufferCapacity</c> messages.
    /// </summary>
    [Test]
    public async Task DispatchAsync_OverflowsBuffer_LogsWarningAndDropsOldest()
    {
        const int seed = 258; // 2 over the 256 cap
        const int expectedRetained = 256;

        var ingestor = new Mock<ISessionEventIngestor>();
        ingestor
            .Setup(i => i.IngestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sb = new StringBuilder();
        for (int i = 0; i < seed; i++)
        {
            var data = JsonSerializer.Serialize(new { questionId = $"q{i}", prompt = "p" });
            sb.Append("event: question_pending\n");
            sb.Append("data: ").Append(data).Append('\n');
            sb.Append('\n');
        }
        // Terminator frame: use a status-update completed so the subscriber can
        // deterministically break out after consuming the retained buffer tail.
        var statusUpdate = JsonSerializer.Serialize(new
        {
            kind = "status-update",
            taskId = "t1",
            contextId = HomespunSessionId,
            status = new { state = "completed", timestamp = "2026-04-24T00:00:00Z" },
            final = true,
        });
        sb.Append("event: status-update\n");
        sb.Append("data: ").Append(statusUpdate).Append('\n');
        sb.Append('\n');

        var logger = new Mock<ILogger<PerSessionEventStream>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        using var handler = new SseResponseHandler(sb.ToString());
        using var httpClient = new HttpClient(handler);
        var stream = new PerSessionEventStream(ingestor.Object, httpClient, logger.Object);

        await stream.StartAsync(HomespunSessionId, WorkerBaseUrl, WorkerSessionId, ProjectId, CancellationToken.None);

        // Wait until the terminating status-update has been processed by the ingestor —
        // by then every preceding question_pending has also been dispatched (and thus
        // buffered / evicted) because DispatchAsync is serial within the read loop.
        await WaitForConditionAsync(() =>
        {
            try
            {
                ingestor.Verify(i => i.IngestAsync(
                    ProjectId, HomespunSessionId, "status-update",
                    It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch (MockException)
            {
                return false;
            }
        });

        // Small yield to ensure the SdkResultMessage produced from the status-update has
        // been enqueued into the pre-subscribe buffer (after the ingestor await).
        await Task.Delay(50);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var messages = new List<SdkMessage>();
        await foreach (var msg in stream.SubscribeTurnAsync(HomespunSessionId, cts.Token))
        {
            messages.Add(msg);
            if (msg is SdkResultMessage) break;
        }

        // 258 question_pending + 1 result = 259 total dispatched, buffer cap 256.
        // The oldest 3 (2 question_pending + oldest... actually: after enqueueing the
        // 257th message the buffer evicts #0; after 258th evicts #1; after the result
        // evicts #2). Net retained: messages [3..257] (255 question_pending) + result =
        // 256 total.
        Assert.That(messages, Has.Count.EqualTo(expectedRetained),
            "buffer must retain exactly PreSubscribeBufferCapacity messages after overflow");
        Assert.That(messages.OfType<SdkResultMessage>().Count(), Is.EqualTo(1),
            "terminating SdkResultMessage must be retained (youngest)");
        Assert.That(messages[^1], Is.InstanceOf<SdkResultMessage>(),
            "SdkResultMessage must be the youngest message retained");

        // Verify Warning was logged at least twice (once per overflow; 3 overflows here).
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2),
            "pre-subscribe buffer overflow must log a Warning per evicted message");

        await stream.DisposeAsync();
    }

    /// <summary>
    /// Regression guard for the DI lifetime bug: <see cref="PerSessionEventStream"/> holds
    /// a per-session reader dictionary, so a transient registration would orphan prior
    /// <c>StartAsync</c> calls whenever a second consumer resolves the service.
    /// <see cref="PerSessionEventStreamServiceCollectionExtensions.AddPerSessionEventStream"/>
    /// must register the service as a singleton — if anyone reverts this, the test fails.
    /// </summary>
    [Test]
    public async Task AddPerSessionEventStream_RegistersServiceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISessionEventIngestor>(Mock.Of<ISessionEventIngestor>());
        services.AddPerSessionEventStream();

        // PerSessionEventStream is IAsyncDisposable-only, so the provider must be
        // disposed asynchronously — synchronous disposal throws at the
        // ServiceProviderEngineScope level.
        await using var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<IPerSessionEventStream>();
        var b = sp.GetRequiredService<IPerSessionEventStream>();

        Assert.That(a, Is.SameAs(b));
    }
}
