using System.Net;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features;

/// <summary>
/// Integration test for the post-result task-notification bug fix.
///
/// <para>
/// Before the <c>fix-post-result-events</c> refactor, an SDK task notification
/// (an A2A <c>message</c> frame) arriving AFTER the SDK <c>result</c> (an A2A
/// <c>status-update</c> with <c>state="completed"</c>, <c>final: true</c>)
/// would buffer in the worker's OutputChannel until the user sent a new prompt.
/// </para>
///
/// <para>
/// The refactor wires every A2A frame straight from the worker's long-lived SSE
/// stream through <see cref="PerSessionEventStream"/> into
/// <see cref="ISessionEventIngestor"/> — which appends to
/// <see cref="IA2AEventStore"/> and broadcasts via SignalR. This test proves the
/// post-result frame still reaches the event store (the SignalR-broadcast
/// source of truth) end-to-end, without any new prompt driving the turn.
/// </para>
///
/// <para>
/// The test swaps the named <c>PerSessionEventStream</c> <see cref="HttpClient"/>
/// for a fake SSE handler that returns three frames: assistant message →
/// status-update completed (result) → post-result task-notification message.
/// It then resolves <see cref="IPerSessionEventStream"/> from the real DI graph,
/// calls <see cref="IPerSessionEventStream.StartAsync"/>, and polls
/// <see cref="IA2AEventStore.ReadAsync"/> until all three frames are persisted.
/// </para>
/// </summary>
[TestFixture]
public class PostResultTaskNotificationTests
{
    private const string SessionId = "test-session-post-result";
    private const string WorkerSessionId = "worker-post-result";
    private const string ProjectId = "proj-post-result";

    [Test]
    public async Task Task_notification_after_result_is_ingested_without_new_prompt()
    {
        var sseBody = BuildSseBody();

        await using var factory = new PerSessionHarness(sseBody);
        var store = factory.Services.GetRequiredService<IA2AEventStore>();
        var stream = factory.Services.GetRequiredService<IPerSessionEventStream>();

        // StartAsync kicks off the background reader. The fake handler returns the pre-baked
        // SSE body and then EOF; the reader dispatches every frame through the real
        // ISessionEventIngestor, which appends to the real IA2AEventStore.
        await stream.StartAsync(
            SessionId,
            "http://fake-worker",
            WorkerSessionId,
            ProjectId,
            CancellationToken.None);

        // Poll until all three A2A events have been persisted. The reader's async
        // pipeline may take a few ms to parse + dispatch + call the ingestor, and
        // the ingestor in turn does translation + SignalR fan-out per event.
        await WaitForConditionAsync(
            async () =>
            {
                var records = await store.ReadAsync(SessionId, since: null, CancellationToken.None);
                return records is not null && records.Count >= 3;
            },
            TimeSpan.FromSeconds(5));

        var records = await store.ReadAsync(SessionId, since: null, CancellationToken.None);

        Assert.That(records, Is.Not.Null, "session event log must exist after ingestion");
        Assert.That(records!.Count, Is.EqualTo(3),
            "expected all three frames (message, status-update, message) to be persisted");
        Assert.That(
            records.Select(r => r.EventKind).ToList(),
            Is.EqualTo(new[] { "message", "status-update", "message" }),
            "events must be persisted in the order they arrived on the SSE stream");

        // The critical assertion: the THIRD record (the post-result task_notification)
        // was persisted — which means ISessionEventIngestor.IngestAsync fired for it
        // despite arriving after the result. That is exactly what the refactor guarantees.
        Assert.That(records[2].EventKind, Is.EqualTo("message"),
            "post-result task_notification must be the third persisted event");
    }

    /// <summary>
    /// Builds an SSE body containing three frames: an assistant agent message, a
    /// status-update carrying <c>completed</c> + <c>final: true</c> (the SDK result),
    /// and a post-result message carrying a <c>task_notification</c> payload.
    /// </summary>
    private static string BuildSseBody()
    {
        var agentMessage = JsonSerializer.Serialize(new
        {
            kind = "message",
            messageId = "m-1",
            role = "agent",
            taskId = "t-1",
            contextId = SessionId,
            parts = new object[]
            {
                new { kind = "text", text = "hi" },
            },
            metadata = new
            {
                sdkMessageType = "assistant",
            },
        });

        var statusUpdate = JsonSerializer.Serialize(new
        {
            kind = "status-update",
            taskId = "t-1",
            contextId = SessionId,
            status = new
            {
                state = "completed",
                timestamp = "2026-04-24T00:00:00Z",
            },
            final = true,
        });

        var taskNotification = JsonSerializer.Serialize(new
        {
            kind = "message",
            messageId = "m-2",
            role = "agent",
            taskId = "t-1",
            contextId = SessionId,
            parts = new object[]
            {
                new
                {
                    kind = "data",
                    data = new { subtype = "task_notification" },
                    metadata = new { kind = "task_notification" },
                },
            },
            metadata = new
            {
                sdkMessageType = "task_notification",
            },
        });

        var sb = new StringBuilder();
        sb.Append("event: message\n");
        sb.Append("data: ").Append(agentMessage).Append('\n');
        sb.Append('\n');
        sb.Append("event: status-update\n");
        sb.Append("data: ").Append(statusUpdate).Append('\n');
        sb.Append('\n');
        sb.Append("event: message\n");
        sb.Append("data: ").Append(taskNotification).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    private static async Task WaitForConditionAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(50);
        }
        Assert.Fail($"Condition not met within {timeout}");
    }

    /// <summary>
    /// A specialization of <see cref="HomespunWebApplicationFactory"/> that swaps
    /// the named <c>PerSessionEventStream</c> <see cref="HttpClient"/> primary
    /// message handler for an in-memory fake SSE handler. The swap happens via
    /// <see cref="IWebHostBuilder.ConfigureTestServices"/> so it wins over the
    /// server-side default registration added by
    /// <see cref="PerSessionEventStreamServiceCollectionExtensions.AddPerSessionEventStream"/>.
    /// </summary>
    private sealed class PerSessionHarness : HomespunWebApplicationFactory
    {
        private readonly string _sseBody;

        public PerSessionHarness(string sseBody)
        {
            _sseBody = sseBody;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                // Register the PerSessionEventStream service. In Mock mode (which
                // HomespunWebApplicationFactory uses by default), the mock-agent path skips
                // this registration — so we opt back in here for the integration surface
                // under test.
                services.AddPerSessionEventStream();

                // Swap the named HttpClient's primary handler for our fake SSE handler.
                // ConfigureTestServices runs after the server's ConfigureServices, so this
                // AddHttpClient call overrides the one that AddPerSessionEventStream made.
                services
                    .AddHttpClient(nameof(PerSessionEventStream))
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeSseHandler(_sseBody));
            });
        }
    }

    private sealed class FakeSseHandler : HttpMessageHandler
    {
        private readonly string _sseBody;

        public FakeSseHandler(string sseBody)
        {
            _sseBody = sseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_sseBody, Encoding.UTF8, "text/event-stream"),
            };
            return Task.FromResult(resp);
        }
    }
}
