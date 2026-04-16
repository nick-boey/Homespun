using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features;

/// <summary>
/// API integration tests for <c>GET /api/sessions/{sessionId}/events</c> (task 4.6).
/// These tests drive the replay endpoint end-to-end against the real
/// <see cref="IA2AEventStore"/> + <see cref="IA2AToAGUITranslator"/> via the web
/// application factory, asserting status codes, ordering, and mode semantics.
/// </summary>
[TestFixture]
public class SessionEventsApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private IA2AEventStore _store = null!;

    private const string ProjectId = "project-events-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [SetUp]
    public void SetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();
        _store = _factory.Services.GetRequiredService<IA2AEventStore>();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static JsonElement TaskPayload(string taskId) =>
        JsonDocument.Parse($$"""
        {
          "kind": "task",
          "id": "{{taskId}}",
          "contextId": "{{taskId}}",
          "status": { "state": "submitted" }
        }
        """).RootElement.Clone();

    private static JsonElement AgentTextMessagePayload(string messageId, string text) =>
        JsonDocument.Parse($$"""
        {
          "kind": "message",
          "messageId": "{{messageId}}",
          "role": "agent",
          "parts": [ { "kind": "text", "text": "{{text}}" } ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """).RootElement.Clone();

    private static JsonElement CompletedStatusUpdatePayload(string taskId) =>
        JsonDocument.Parse($$"""
        {
          "kind": "status-update",
          "taskId": "{{taskId}}",
          "contextId": "{{taskId}}",
          "status": {
            "state": "completed",
            "message": {
              "kind": "message",
              "messageId": "final",
              "role": "agent",
              "parts": [ { "kind": "text", "text": "Done" } ]
            }
          },
          "final": true
        }
        """).RootElement.Clone();

    // ---------------- 4.3: happy path (seq-ascending) ----------------

    [Test]
    public async Task GetEvents_ReturnsEnvelopesInSeqOrder()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        await _store.AppendAsync(ProjectId, sessionId, "task", TaskPayload("t1"));
        await _store.AppendAsync(ProjectId, sessionId, "message", AgentTextMessagePayload("m1", "Hello"));
        await _store.AppendAsync(ProjectId, sessionId, "status-update", CompletedStatusUpdatePayload("t1"));

        var envelopes = await GetEnvelopesAsync(sessionId);

        Assert.That(envelopes, Is.Not.Null);
        Assert.That(envelopes!.Count, Is.GreaterThan(0));

        // Seq must be non-decreasing across the entire response.
        for (var i = 1; i < envelopes.Count; i++)
        {
            Assert.That(envelopes[i].Seq, Is.GreaterThanOrEqualTo(envelopes[i - 1].Seq),
                "envelopes must be returned in seq-ascending order");
        }

        // First envelope is the RunStarted produced by the Task translation.
        var first = envelopes[0];
        Assert.That(first.Seq, Is.EqualTo(1));
        Assert.That(first.SessionId, Is.EqualTo(sessionId));
    }

    // ---------------- 4.4: unknown session → 404 ----------------

    [Test]
    public async Task GetEvents_UnknownSession_Returns404()
    {
        var response = await _client.GetAsync("/api/sessions/does-not-exist/events");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // ---------------- 4.5: since beyond end → 200 + empty array ----------------

    [Test]
    public async Task GetEvents_SinceBeyondEnd_Returns200WithEmptyArray()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        await _store.AppendAsync(ProjectId, sessionId, "task", TaskPayload("t1"));
        await _store.AppendAsync(ProjectId, sessionId, "message", AgentTextMessagePayload("m1", "Hi"));

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/events?since=9999");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var envelopes = await response.Content.ReadFromJsonAsync<List<SessionEventEnvelope>>(JsonOptions);
        Assert.That(envelopes, Is.Not.Null.And.Empty,
            "since-beyond-end must be a caught-up client signal, not an error");
    }

    // ---------------- Mode semantics ----------------

    [Test]
    public async Task GetEvents_IncrementalMode_ReturnsOnlyEnvelopesAfterSince()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        await _store.AppendAsync(ProjectId, sessionId, "task", TaskPayload("t1"));           // seq 1
        await _store.AppendAsync(ProjectId, sessionId, "message", AgentTextMessagePayload("m1", "A"));  // seq 2
        await _store.AppendAsync(ProjectId, sessionId, "message", AgentTextMessagePayload("m2", "B"));  // seq 3

        var envelopes = await GetEnvelopesAsync(sessionId, since: 1, mode: "incremental");

        Assert.That(envelopes, Is.Not.Null);
        // Everything from seq > 1; at minimum no seq=1 envelope should appear.
        Assert.That(envelopes!.Any(e => e.Seq == 1), Is.False,
            "incremental since=1 must exclude seq-1 envelopes");
        Assert.That(envelopes.All(e => e.Seq >= 2), Is.True);
    }

    [Test]
    public async Task GetEvents_FullMode_IgnoresSinceAndReturnsAll()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        await _store.AppendAsync(ProjectId, sessionId, "task", TaskPayload("t1"));
        await _store.AppendAsync(ProjectId, sessionId, "message", AgentTextMessagePayload("m1", "A"));

        var envelopes = await GetEnvelopesAsync(sessionId, since: 999, mode: "full");

        Assert.That(envelopes, Is.Not.Null);
        Assert.That(envelopes!.Any(e => e.Seq == 1), Is.True,
            "mode=full must ignore since and return seq-1 envelopes");
    }

    // ---------------- Envelope invariants ----------------

    [Test]
    public async Task GetEvents_EveryEnvelopeCarriesSessionIdAndEventId()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        await _store.AppendAsync(ProjectId, sessionId, "task", TaskPayload("t1"));

        var envelopes = await GetEnvelopesAsync(sessionId);

        Assert.That(envelopes, Is.Not.Null.And.Not.Empty);
        foreach (var env in envelopes!)
        {
            Assert.Multiple(() =>
            {
                Assert.That(env.SessionId, Is.EqualTo(sessionId));
                Assert.That(env.EventId, Is.Not.Null.And.Not.Empty);
                Assert.That(Guid.TryParse(env.EventId, out _), Is.True,
                    $"eventId '{env.EventId}' must be a valid GUID");
            });
        }
    }

    // ---------------- Helpers ----------------

    private async Task<List<SessionEventEnvelope>?> GetEnvelopesAsync(
        string sessionId, long? since = null, string? mode = null)
    {
        var query = new List<string>();
        if (since.HasValue) query.Add($"since={since.Value}");
        if (!string.IsNullOrEmpty(mode)) query.Add($"mode={mode}");
        var url = $"/api/sessions/{sessionId}/events" +
                  (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);

        var response = await _client.GetAsync(url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"GET {url} returned {response.StatusCode}");

        return await response.Content.ReadFromJsonAsync<List<SessionEventEnvelope>>(JsonOptions);
    }
}
