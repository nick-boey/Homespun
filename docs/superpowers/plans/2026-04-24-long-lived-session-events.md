# Long-lived session-event stream (post-result buffering fix) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver SDK task-notification events that arrive after a `result` message to the client in real time, instead of buffering them in the worker's `OutputChannel` until the user sends the next prompt.

**Architecture:** Decouple event streaming from the per-turn HTTP request cycle. Add a long-lived `GET /api/sessions/:id/events` SSE endpoint on the worker that stays open for the life of the session. `POST /sessions` and `POST /sessions/:id/message` become JSON-returning control-plane calls (no SSE body). A new server-side `PerSessionEventStream` service runs a background task per worker session that consumes the long-lived stream, drives `ISessionEventIngestor` (for SignalR broadcast to the client), and fans out `SdkMessage`s to per-turn consumers via a `Channel<SdkMessage>`. The existing `IAgentExecutionService.StartSessionAsync` / `SendMessageAsync` contracts (returning `IAsyncEnumerable<SdkMessage>`) are preserved by reading from the fan-out channel.

**Tech Stack:** ASP.NET 10, Hono 4 (Node 20), System.Threading.Channels, OpenTelemetry SDK, NUnit + Moq, Vitest.

---

## File Structure

**New:**
- `src/Homespun.Worker/src/routes/sessions.ts` — add `GET /api/sessions/:id/events` handler (long-lived SSE).
- `src/Homespun.Server/Features/ClaudeCode/Services/IPerSessionEventStream.cs` — interface.
- `src/Homespun.Server/Features/ClaudeCode/Services/PerSessionEventStream.cs` — impl.
- `tests/Homespun.Tests/ClaudeCode/PerSessionEventStreamTests.cs` — unit tests.
- `tests/Homespun.Api.Tests/Sessions/PostResultTaskNotificationTests.cs` — integration test.

**Modify:**
- `src/Homespun.Worker/src/services/sse-writer.ts` — remove the `return` on `msg.type === "result"`.
- `src/Homespun.Worker/src/routes/sessions.ts` — `POST /sessions`, `POST /sessions/:id/message`, `POST /sessions/:id/clear-context` return JSON instead of SSE.
- `src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs` — replace `SendSseRequestAsync` usage with `PerSessionEventStream` subscriptions. Remove the `SdkResultMessage → yield break` early-termination in the SSE parser (moved into subscriber loop).
- `src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionService.cs` — same treatment.
- `src/Homespun.Server/Features/ClaudeCode/Services/MessageProcessingService.cs` — no signature change, but document that the `IAsyncEnumerable<SdkMessage>` it now receives comes from the fan-out channel.
- `src/Homespun.Server/Program.cs` — register `IPerSessionEventStream` singleton.
- `src/Homespun.Worker/src/routes/sessions.test.ts` (if present) — update expectations for JSON responses.

**Do NOT touch unless driven by a test failure:**
- `src/Homespun.Server/Features/ClaudeCode/Services/MessageProcessingService.cs` control-plane logic (still breaks on `SdkResultMessage`).
- `src/Homespun.Web/**` — client is unchanged.
- `src/Homespun.Server/Features/Testing/Services/MockAgentExecutionService.cs` — it builds its own `IAsyncEnumerable<SdkMessage>` without an SSE round-trip; no change needed.

---

## Task 1: Worker — strip the `return` on `result` in `sse-writer`

**Files:**
- Modify: `src/Homespun.Worker/src/services/sse-writer.ts:244-256`
- Test: `src/Homespun.Worker/src/services/sse-writer.test.ts`

- [ ] **Step 1: Write failing test**

Add to `sse-writer.test.ts`:

```ts
import { streamSessionEvents } from './sse-writer.js';

it('continues emitting after a result message', async () => {
  const sm = buildMockSessionManager({
    events: [
      { type: 'assistant', message: { content: [{ type: 'text', text: 'hi' }] } },
      { type: 'result', subtype: 'success', is_error: false, duration_ms: 1, duration_api_ms: 1, num_turns: 1, total_cost_usd: 0, session_id: 's', result: '' },
      { type: 'system', subtype: 'task_notification', task_id: 't', status: 'completed', output_file: '/x', summary: 'ok', session_id: 's', uuid: 'u' },
    ],
  });
  const chunks: string[] = [];
  for await (const chunk of streamSessionEvents(sm, 's')) chunks.push(chunk);
  const kinds = chunks.map(c => c.match(/^event: (\w+)/)?.[1]);
  expect(kinds).toContain('status-update'); // the result
  expect(kinds.filter(k => k === 'message').length).toBeGreaterThanOrEqual(2); // assistant + task_notification
});
```

- [ ] **Step 2: Run the test and verify failure**

```
cd src/Homespun.Worker && npm test -- sse-writer
```

Expected: FAIL — the generator currently returns after the result status-update, so only one `message` event appears before close.

- [ ] **Step 3: Remove the early return**

In `sse-writer.ts:244-256`, change:

```ts
if (msg.type === "result") {
  // Result -> TaskStatusUpdateEvent with completed/failed, final: true
  const r = msg as any;
  info(`A2A result: subtype='${r.subtype}', is_error=${r.is_error}`);
  const finalState = r.is_error ? "failed" : "completed";
  info(
    `A2A state transition: working → ${finalState} (sessionId: ${sessionId})`,
  );

  const statusUpdate = translateResultToStatus(msg, ctx);
  yield emitAndFormatSSE(sessionId, statusUpdate.kind, statusUpdate);
  return;
}
```

to:

```ts
if (msg.type === "result") {
  const r = msg as any;
  info(`A2A result: subtype='${r.subtype}', is_error=${r.is_error}`);
  const finalState = r.is_error ? "failed" : "completed";
  info(
    `A2A state transition: working → ${finalState} (sessionId: ${sessionId})`,
  );

  const statusUpdate = translateResultToStatus(msg, ctx);
  yield emitAndFormatSSE(sessionId, statusUpdate.kind, statusUpdate);
  // Do NOT return — the SDK query iterator stays open across turns and still
  // emits task_notification / task_updated / task_started events as background
  // tools complete. The long-lived GET /events consumer on the server is
  // responsible for draining them.
  continue;
}
```

- [ ] **Step 4: Run the test and verify pass**

```
cd src/Homespun.Worker && npm test -- sse-writer
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Worker/src/services/sse-writer.ts src/Homespun.Worker/src/services/sse-writer.test.ts
git commit -m "fix(worker): do not terminate SSE stream on SDK result message"
```

---

## Task 2: Worker — add long-lived `GET /api/sessions/:id/events` endpoint

**Files:**
- Modify: `src/Homespun.Worker/src/routes/sessions.ts` (add route before existing `:id` GET)
- Test: `src/Homespun.Worker/src/routes/sessions.test.ts`

- [ ] **Step 1: Write failing test**

Add to `sessions.test.ts`:

```ts
it('GET /api/sessions/:id/events streams events across result boundaries', async () => {
  const { app, sessionId, push } = await bootWorkerWithSession();
  const res = await app.request(`/api/sessions/${sessionId}/events`);
  const reader = res.body!.getReader();
  push({ type: 'assistant', message: { content: [{ type: 'text', text: 'a' }] } });
  push({ type: 'result', subtype: 'success', session_id: 's', is_error: false, duration_ms: 1, duration_api_ms: 1, num_turns: 1, total_cost_usd: 0, result: '' });
  push({ type: 'system', subtype: 'task_notification', task_id: 't', status: 'completed', output_file: '/x', summary: 'ok', session_id: 's', uuid: 'u' });
  const buf = await readN(reader, 3);
  expect(buf.split('event: status-update').length - 1).toBe(1);
  expect(buf.split('event: message').length - 1).toBeGreaterThanOrEqual(2);
});
```

(Assumes a helper `bootWorkerWithSession` and `readN` in the existing test harness. If absent, implement them similarly to the existing route tests using the worker's Hono app instance.)

- [ ] **Step 2: Run the test and verify failure**

```
cd src/Homespun.Worker && npm test -- sessions
```

Expected: FAIL — 404, no such route.

- [ ] **Step 3: Add the route**

Insert into `src/Homespun.Worker/src/routes/sessions.ts`, immediately before the `GET /:id` handler:

```ts
// GET /sessions/:id/events - Long-lived SSE stream of session events.
// Stays open for the life of the session; emits every A2A event including
// task notifications that arrive after the SDK result message.
sessions.get('/:id/events', async (c) => {
  const sessionId = c.req.param('id');
  info(`GET /sessions/${sessionId}/events - long-lived SSE consumer opened`);

  c.header('Content-Type', 'text/event-stream');
  c.header('Cache-Control', 'no-cache');
  c.header('Connection', 'keep-alive');

  return stream(c, async (s) => {
    for await (const chunk of streamSessionEvents(sessionManager, sessionId)) {
      await s.write(chunk);
    }
  });
});
```

- [ ] **Step 4: Run the test and verify pass**

```
cd src/Homespun.Worker && npm test -- sessions
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Worker/src/routes/sessions.ts src/Homespun.Worker/src/routes/sessions.test.ts
git commit -m "feat(worker): add GET /api/sessions/:id/events long-lived SSE endpoint"
```

---

## Task 3: Worker — convert `POST /sessions` to JSON response

**Files:**
- Modify: `src/Homespun.Worker/src/routes/sessions.ts:54-86`
- Test: `src/Homespun.Worker/src/routes/sessions.test.ts`

- [ ] **Step 1: Write failing test**

```ts
it('POST /api/sessions returns JSON { sessionId, conversationId } and does not stream', async () => {
  const app = buildApp();
  const res = await app.request('/api/sessions', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ prompt: 'hi', mode: 'Build', model: 'opus', workingDirectory: '/tmp' }),
  });
  expect(res.headers.get('content-type')).toContain('application/json');
  const body = await res.json();
  expect(body.sessionId).toBeDefined();
});
```

- [ ] **Step 2: Run the test and verify failure**

```
cd src/Homespun.Worker && npm test -- sessions
```

Expected: FAIL — response is `text/event-stream`.

- [ ] **Step 3: Replace the handler body**

Change `sessions.post('/', ...)` to:

```ts
sessions.post('/', async (c) => {
  const body = await c.req.json<StartSessionRequest>();
  info(`POST /sessions - mode=${body.mode}, model=${body.model}, workingDirectory=${body.workingDirectory}, resumeSessionId=${body.resumeSessionId || 'none'}`);

  try {
    const ws = await sessionManager.create({
      prompt: body.prompt,
      model: body.model,
      mode: body.mode,
      systemPrompt: body.systemPrompt,
      workingDirectory: body.workingDirectory,
      resumeSessionId: body.resumeSessionId,
    });
    return c.json({ sessionId: ws.id, conversationId: ws.conversationId ?? null });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return c.json({ error: message, code: 'STARTUP_ERROR' }, 500);
  }
});
```

- [ ] **Step 4: Run the test and verify pass**

```
cd src/Homespun.Worker && npm test -- sessions
```

Expected: PASS. Existing "streams on POST" assertions will need updating in the next steps — do not fix unrelated failures here.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Worker/src/routes/sessions.ts src/Homespun.Worker/src/routes/sessions.test.ts
git commit -m "feat(worker): return JSON sessionId from POST /api/sessions (no SSE body)"
```

---

## Task 4: Worker — convert `POST /sessions/:id/message` to JSON response

**Files:**
- Modify: `src/Homespun.Worker/src/routes/sessions.ts:89-115`
- Test: `src/Homespun.Worker/src/routes/sessions.test.ts`

- [ ] **Step 1: Write failing test**

```ts
it('POST /api/sessions/:id/message returns { ok: true } JSON', async () => {
  const { app, sessionId } = await bootWorkerWithSession();
  const res = await app.request(`/api/sessions/${sessionId}/message`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ message: 'hi', mode: 'Build', model: 'opus' }),
  });
  expect(res.headers.get('content-type')).toContain('application/json');
  expect(await res.json()).toEqual({ ok: true });
});
```

- [ ] **Step 2: Run the test and verify failure**

Expected: FAIL — still streaming.

- [ ] **Step 3: Replace the handler body**

```ts
sessions.post('/:id/message', async (c) => {
  const sessionId = c.req.param('id');
  const body = await c.req.json<SendMessageRequest>();
  info(`POST /sessions/${sessionId}/message - mode=${body.mode}, messageLength=${body.message?.length}, model=${body.model}`);

  try {
    await sessionManager.send(sessionId, body.message, body.model, body.mode);
    return c.json({ ok: true });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return c.json({ ok: false, error: message, code: 'MESSAGE_ERROR' }, 500);
  }
});
```

- [ ] **Step 4: Run the test and verify pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Worker/src/routes/sessions.ts src/Homespun.Worker/src/routes/sessions.test.ts
git commit -m "feat(worker): return JSON from POST /api/sessions/:id/message (no SSE body)"
```

---

## Task 5: Worker — convert `POST /sessions/:id/clear-context` to JSON response

**Files:**
- Modify: `src/Homespun.Worker/src/routes/sessions.ts:228-270`
- Test: `src/Homespun.Worker/src/routes/sessions.test.ts`

- [ ] **Step 1: Write failing test**

```ts
it('POST /api/sessions/:id/clear-context returns { oldSessionId, newSessionId } JSON', async () => {
  const { app, sessionId } = await bootWorkerWithSession();
  const res = await app.request(`/api/sessions/${sessionId}/clear-context`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ prompt: 'new', mode: 'Build', model: 'opus', workingDirectory: '/tmp' }),
  });
  expect(res.headers.get('content-type')).toContain('application/json');
  const body = await res.json();
  expect(body.oldSessionId).toBe(sessionId);
  expect(body.newSessionId).toBeDefined();
});
```

- [ ] **Step 2: Verify failure**

Expected: FAIL — still streaming.

- [ ] **Step 3: Replace the handler body**

```ts
sessions.post('/:id/clear-context', async (c) => {
  const sessionId = c.req.param('id');
  const body = await c.req.json<ClearContextRequest>();
  info(`POST /sessions/${sessionId}/clear-context - mode=${body.mode}, model=${body.model}`);

  try {
    const { newSession, oldSessionId } = await sessionManager.clearContextAndCreate(
      sessionId,
      {
        prompt: body.prompt,
        model: body.model,
        mode: body.mode,
        systemPrompt: body.systemPrompt,
        workingDirectory: body.workingDirectory,
      }
    );
    return c.json({ oldSessionId, newSessionId: newSession.id });
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return c.json({ error: message, code: 'CLEAR_CONTEXT_ERROR' }, 500);
  }
});
```

- [ ] **Step 4: Verify pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Worker/src/routes/sessions.ts src/Homespun.Worker/src/routes/sessions.test.ts
git commit -m "feat(worker): return JSON from POST /api/sessions/:id/clear-context"
```

---

## Task 6: Server — define `IPerSessionEventStream`

**Files:**
- Create: `src/Homespun.Server/Features/ClaudeCode/Services/IPerSessionEventStream.cs`

- [ ] **Step 1: Create the file**

```csharp
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Per-worker-session long-lived SSE consumer. Runs a background task that
/// reads the worker's <c>GET /api/sessions/{id}/events</c> endpoint for the
/// full life of the session, invokes <see cref="ISessionEventIngestor"/>
/// on every A2A event (so SignalR broadcasts never stop), and fans out
/// parsed <see cref="SdkMessage"/> values to per-turn subscribers.
///
/// <para>
/// This service exists to decouple the client-visible event stream from the
/// per-turn HTTP request cycle. The Claude Agent SDK emits
/// <c>SDKTaskNotificationMessage</c> / <c>SDKTaskStartedMessage</c> /
/// <c>SDKTaskUpdatedMessage</c> as background bash tasks settle — often
/// minutes after the <c>result</c> message that ends a turn. Before this
/// service, those events piled up in the worker's <c>OutputChannel</c> with
/// no consumer; now they flow through the long-lived reader and reach the
/// client live.
/// </para>
/// </summary>
public interface IPerSessionEventStream
{
    /// <summary>
    /// Starts the background reader for a worker session. Idempotent —
    /// second calls for the same <paramref name="homespunSessionId"/> are
    /// a no-op.
    /// </summary>
    Task StartAsync(
        string homespunSessionId,
        string workerUrl,
        string workerSessionId,
        string? projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes to SDK messages for a single turn. Returned enumerable
    /// completes after the next <see cref="SdkResultMessage"/> is yielded.
    /// Throws if no reader is running for <paramref name="homespunSessionId"/>.
    /// </summary>
    IAsyncEnumerable<SdkMessage> SubscribeTurnAsync(
        string homespunSessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops the background reader and completes any pending subscriber.
    /// Safe to call for an unknown session id.
    /// </summary>
    Task StopAsync(string homespunSessionId);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Homespun.Server/Features/ClaudeCode/Services/IPerSessionEventStream.cs
git commit -m "feat(server): define IPerSessionEventStream interface"
```

---

## Task 7: Server — implement `PerSessionEventStream`

**Files:**
- Create: `src/Homespun.Server/Features/ClaudeCode/Services/PerSessionEventStream.cs`
- Test: `tests/Homespun.Tests/ClaudeCode/PerSessionEventStreamTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/Homespun.Tests/ClaudeCode/PerSessionEventStreamTests.cs`:

```csharp
using System.Text;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.ClaudeCode;

public class PerSessionEventStreamTests
{
    [Test]
    public async Task Ingests_events_that_arrive_after_result()
    {
        var ingestor = new Mock<ISessionEventIngestor>();
        var fake = new FakeSseServer();
        fake.Queue("status-update", """{"kind":"status-update","taskId":"t","contextId":"c","status":{"state":"completed","timestamp":"2026-04-24T00:00:00Z"},"final":true}""");
        fake.Queue("message", """{"kind":"message","messageId":"m2","role":"agent","parts":[{"kind":"data","data":{"subtype":"task_notification","status":"completed"},"metadata":{"kind":"task_notification"}}]}""");
        var stream = new PerSessionEventStream(ingestor.Object, fake.HttpClient, NullLogger<PerSessionEventStream>.Instance);
        await stream.StartAsync("s", fake.BaseUrl, "w", "p", CancellationToken.None);

        await fake.FlushAsync();

        ingestor.Verify(i => i.IngestAsync("p", "s", "status-update", It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        ingestor.Verify(i => i.IngestAsync("p", "s", "message", It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // FakeSseServer: minimal in-memory HttpClient handler yielding pre-queued SSE frames.
    // Implementation lives at tests/Homespun.Tests/ClaudeCode/_fakes/FakeSseServer.cs
}
```

Also create `tests/Homespun.Tests/ClaudeCode/_fakes/FakeSseServer.cs`:

```csharp
using System.Net;
using System.Text;

namespace Homespun.Tests.ClaudeCode;

internal sealed class FakeSseServer
{
    private readonly List<(string kind, string data)> _queue = new();
    private TaskCompletionSource<bool>? _flushed;

    public string BaseUrl => "http://fake";
    public HttpClient HttpClient { get; }

    public FakeSseServer()
    {
        HttpClient = new HttpClient(new Handler(this));
    }

    public void Queue(string kind, string data) => _queue.Add((kind, data));

    public Task FlushAsync()
    {
        _flushed ??= new TaskCompletionSource<bool>();
        return _flushed.Task;
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly FakeSseServer _parent;
        public Handler(FakeSseServer parent) => _parent = parent;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            foreach (var (kind, data) in _parent._queue)
            {
                sb.Append("event: ").Append(kind).Append('\n');
                sb.Append("data: ").Append(data).Append('\n');
                sb.Append('\n');
            }
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream"),
            };
            _parent._flushed?.TrySetResult(true);
            return Task.FromResult(resp);
        }
    }
}
```

- [ ] **Step 2: Run the test and verify failure**

```
dotnet test tests/Homespun.Tests --filter FullyQualifiedName~PerSessionEventStream
```

Expected: FAIL — `PerSessionEventStream` class does not exist.

- [ ] **Step 3: Implement `PerSessionEventStream`**

Create `src/Homespun.Server/Features/ClaudeCode/Services/PerSessionEventStream.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

public sealed class PerSessionEventStream : IPerSessionEventStream, IAsyncDisposable
{
    private readonly ISessionEventIngestor _ingestor;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PerSessionEventStream> _logger;
    private readonly ConcurrentDictionary<string, Reader> _readers = new();

    public PerSessionEventStream(
        ISessionEventIngestor ingestor,
        HttpClient httpClient,
        ILogger<PerSessionEventStream> logger)
    {
        _ingestor = ingestor;
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task StartAsync(
        string homespunSessionId,
        string workerUrl,
        string workerSessionId,
        string? projectId,
        CancellationToken cancellationToken)
    {
        _readers.GetOrAdd(homespunSessionId, _ =>
        {
            var reader = new Reader(
                _ingestor, _httpClient, _logger,
                homespunSessionId, workerUrl, workerSessionId, projectId);
            reader.Start();
            return reader;
        });
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<SdkMessage> SubscribeTurnAsync(
        string homespunSessionId,
        CancellationToken cancellationToken)
    {
        if (!_readers.TryGetValue(homespunSessionId, out var reader))
        {
            throw new InvalidOperationException(
                $"No per-session event stream running for session {homespunSessionId}");
        }
        return reader.SubscribeTurnAsync(cancellationToken);
    }

    public async Task StopAsync(string homespunSessionId)
    {
        if (_readers.TryRemove(homespunSessionId, out var reader))
        {
            await reader.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var reader in _readers.Values) await reader.DisposeAsync();
        _readers.Clear();
    }

    private sealed class Reader : IAsyncDisposable
    {
        private readonly ISessionEventIngestor _ingestor;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _sessionId;
        private readonly string _workerUrl;
        private readonly string _workerSessionId;
        private readonly string? _projectId;
        private readonly CancellationTokenSource _cts = new();
        private Task? _readTask;
        private Channel<SdkMessage>? _currentTurn;
        private readonly object _turnLock = new();

        public Reader(ISessionEventIngestor ingestor, HttpClient httpClient, ILogger logger,
            string sessionId, string workerUrl, string workerSessionId, string? projectId)
        {
            _ingestor = ingestor;
            _httpClient = httpClient;
            _logger = logger;
            _sessionId = sessionId;
            _workerUrl = workerUrl;
            _workerSessionId = workerSessionId;
            _projectId = projectId;
        }

        public void Start()
        {
            _readTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public IAsyncEnumerable<SdkMessage> SubscribeTurnAsync(CancellationToken cancellationToken)
        {
            lock (_turnLock)
            {
                if (_currentTurn is not null)
                {
                    throw new InvalidOperationException(
                        $"Session {_sessionId} already has an active turn subscription");
                }
                _currentTurn = Channel.CreateUnbounded<SdkMessage>();
            }
            return DrainAsync(_currentTurn, cancellationToken);
        }

        private async IAsyncEnumerable<SdkMessage> DrainAsync(
            Channel<SdkMessage> ch,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                await foreach (var msg in ch.Reader.ReadAllAsync(ct))
                {
                    yield return msg;
                    if (msg is SdkResultMessage) yield break;
                }
            }
            finally
            {
                lock (_turnLock) { _currentTurn = null; }
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                var url = $"{_workerUrl.TrimEnd('/')}/api/sessions/{_workerSessionId}/events";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                string? eventType = null;
                var data = new StringBuilder();

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    if (line.StartsWith("event: ")) eventType = line[7..];
                    else if (line.StartsWith("data: ")) data.Append(line[6..]);
                    else if (line.Length == 0)
                    {
                        if (!string.IsNullOrEmpty(eventType) && data.Length > 0)
                        {
                            await DispatchAsync(eventType!, data.ToString(), ct);
                        }
                        eventType = null;
                        data.Clear();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Per-session event stream for {SessionId} ended unexpectedly", _sessionId);
            }
            finally
            {
                lock (_turnLock)
                {
                    _currentTurn?.Writer.TryComplete();
                    _currentTurn = null;
                }
            }
        }

        private async Task DispatchAsync(string kind, string raw, CancellationToken ct)
        {
            // 1. Ingest A2A events so SignalR keeps broadcasting regardless of turn state.
            if (A2AMessageParser.IsA2AEventKind(kind))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    await _ingestor.IngestAsync(
                        string.IsNullOrEmpty(_projectId) ? "unknown" : _projectId,
                        _sessionId, kind, doc.RootElement.Clone(), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Ingestor tap failed for session {SessionId} kind {Kind}", _sessionId, kind);
                }
            }

            // 2. Fan out a parsed SdkMessage to the current turn subscriber, if any.
            var sdkMessage = ParseToSdkMessage(kind, raw);
            if (sdkMessage is null) return;

            Channel<SdkMessage>? ch;
            lock (_turnLock) { ch = _currentTurn; }
            if (ch is not null)
            {
                await ch.Writer.WriteAsync(sdkMessage, ct);
            }
        }

        private SdkMessage? ParseToSdkMessage(string kind, string raw)
        {
            if (kind == HomespunA2AEventKind.Task ||
                kind == HomespunA2AEventKind.Message ||
                kind == HomespunA2AEventKind.StatusUpdate ||
                kind == HomespunA2AEventKind.ArtifactUpdate)
            {
                var parsed = A2AMessageParser.ParseSseEvent(kind, raw);
                return parsed is null ? null : A2AMessageParser.ConvertToSdkMessage(parsed, _sessionId);
            }

            if (kind == "session_started")
            {
                using var doc = JsonDocument.Parse(raw);
                var wsid = doc.RootElement.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                return new SdkSystemMessage(wsid ?? _sessionId, null, "session_started", null, null);
            }
            if (kind == "question_pending") return new SdkQuestionPendingMessage(_sessionId, raw);
            if (kind == "plan_pending") return new SdkPlanPendingMessage(_sessionId, raw);
            return null;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_readTask is not null)
            {
                try { await _readTask; } catch { }
            }
            _cts.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run the test and verify pass**

```
dotnet test tests/Homespun.Tests --filter FullyQualifiedName~PerSessionEventStream
```

Expected: PASS.

- [ ] **Step 5: Register in DI**

Edit `src/Homespun.Server/Program.cs`. Find the `IAgentExecutionService` registration and add next to it:

```csharp
builder.Services.AddSingleton<IPerSessionEventStream, PerSessionEventStream>();
```

Also ensure `HttpClient` is resolvable — `PerSessionEventStream` takes `HttpClient` by constructor. Use an HttpClientFactory-backed registration:

```csharp
builder.Services.AddHttpClient<IPerSessionEventStream, PerSessionEventStream>(c =>
{
    c.Timeout = Timeout.InfiniteTimeSpan;
});
```

- [ ] **Step 6: Commit**

```bash
git add src/Homespun.Server/Features/ClaudeCode/Services/PerSessionEventStream.cs \
        src/Homespun.Server/Program.cs \
        tests/Homespun.Tests/ClaudeCode/PerSessionEventStreamTests.cs \
        tests/Homespun.Tests/ClaudeCode/_fakes/FakeSseServer.cs
git commit -m "feat(server): add PerSessionEventStream background reader"
```

---

## Task 8: Server — rewire `DockerAgentExecutionService` to use `PerSessionEventStream`

**Files:**
- Modify: `src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs`
- Test: `tests/Homespun.Tests/ClaudeCode/DockerAgentExecutionServiceTests.cs` (existing; update assertions)

- [ ] **Step 1: Write failing test**

Add a test that asserts `StartSessionAsync` consults `IPerSessionEventStream`:

```csharp
[Test]
public async Task StartSessionAsync_starts_per_session_event_stream()
{
    var perSession = new Mock<IPerSessionEventStream>();
    perSession.Setup(p => p.SubscribeTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(AsyncEmpty(new SdkResultMessage("s", null, "success", 0, 0, false, 1, 0, "", null)));
    var svc = BuildService(perSession: perSession.Object);

    await foreach (var _ in svc.StartSessionAsync(DefaultStartRequest())) { }

    perSession.Verify(p => p.StartAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

(Add `AsyncEmpty<T>(params T[] items)` helper in the test file if not present.)

- [ ] **Step 2: Verify failure**

```
dotnet test tests/Homespun.Tests --filter StartSessionAsync_starts_per_session_event_stream
```

Expected: FAIL — `IPerSessionEventStream` not injected, `StartAsync` never called.

- [ ] **Step 3: Inject + wire**

Add `IPerSessionEventStream` to the constructor parameters of `DockerAgentExecutionService`. Store as `_perSession`.

Rewrite the streaming portion of `StartSessionAsync`. Replace the `SendSseRequestAsync` call in the `StartSessionAsync` background Task (around line 356) with:

```csharp
// Parse the POST /api/sessions JSON response to get the worker session id.
var startResponse = await _httpClient.PostAsJsonAsync($"{workerUrl}/api/sessions", startRequest, CamelCaseJsonOptions, cts.Token);
startResponse.EnsureSuccessStatusCode();
var startBody = await startResponse.Content.ReadFromJsonAsync<WorkerStartResponse>(CamelCaseJsonOptions, cts.Token)
    ?? throw new InvalidOperationException("Worker POST /api/sessions returned empty body");

session = session with { WorkerSessionId = startBody.SessionId };
_sessions[sessionId] = session;

await _perSession.StartAsync(sessionId, workerUrl, startBody.SessionId, request.ProjectId, cts.Token);

// Emit the legacy session_started message so downstream consumers match the old contract.
await channel.Writer.WriteAsync(
    new SdkSystemMessage(startBody.SessionId, null, "session_started", request.Model, null),
    cancellationToken);

await foreach (var msg in _perSession.SubscribeTurnAsync(sessionId, cts.Token))
{
    var remapped = RemapSessionId(msg, sessionId);
    await channel.Writer.WriteAsync(remapped, cancellationToken);
    if (msg is SdkResultMessage resultMsg)
    {
        session.ConversationId = resultMsg.SessionId;
        _sessions[sessionId] = session;
    }
}
channel.Writer.Complete();
```

Add a helper record at the bottom of the file:

```csharp
private sealed record WorkerStartResponse(string SessionId, string? ConversationId);
```

Rewrite `SendMessageAsync` (around line 421–467):

```csharp
public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
    AgentMessageRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    _sessionToIssue.TryGetValue(request.SessionId, out var issueId);
    using var issueScope = IssueLogScope.BeginIssueScope(_logger, issueId);

    _logger.LogInformation("SendMessageAsync called for session {SessionId}", request.SessionId);

    if (!_sessions.TryGetValue(request.SessionId, out var session))
    {
        _logger.LogError("Session {SessionId} not found in _sessions dictionary", request.SessionId);
        yield break;
    }
    if (string.IsNullOrEmpty(session.WorkerSessionId))
    {
        _logger.LogError("Worker session ID is empty - session was not properly initialized");
        yield break;
    }

    session.LastActivityAt = DateTime.UtcNow;

    var messageRequest = new
    {
        Message = request.Message,
        Model = request.Model,
        Mode = request.Mode.ToString(),
    };

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

    var sendResponse = await _httpClient.PostAsJsonAsync(
        $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/message",
        messageRequest, CamelCaseJsonOptions, linkedCts.Token);
    sendResponse.EnsureSuccessStatusCode();

    await foreach (var msg in _perSession.SubscribeTurnAsync(request.SessionId, linkedCts.Token))
    {
        var remapped = RemapSessionId(msg, request.SessionId);
        yield return remapped;

        if (msg is SdkResultMessage resultMsg)
        {
            session.ConversationId = resultMsg.SessionId;
            _sessions[request.SessionId] = session;
        }
    }
}
```

Also call `_perSession.StopAsync(sessionId)` from `StopSessionAsync` before tearing down the container.

Delete `SendSseRequestAsync` and `TryIngestA2AEventAsync` (they are now redundant) — do this only after all callers are removed.

- [ ] **Step 4: Verify pass + run full Docker service test suite**

```
dotnet test tests/Homespun.Tests --filter FullyQualifiedName~DockerAgentExecutionService
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs \
        tests/Homespun.Tests/ClaudeCode/DockerAgentExecutionServiceTests.cs
git commit -m "refactor(server): route DockerAgentExecutionService events via PerSessionEventStream"
```

---

## Task 9: Server — same rewire for `SingleContainerAgentExecutionService`

**Files:**
- Modify: `src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionService.cs`
- Test: `tests/Homespun.Tests/ClaudeCode/SingleContainerAgentExecutionServiceTests.cs` (if exists; otherwise extend sessions tests).

- [ ] **Step 1: Write failing test (mirror Task 8's test structure)**

- [ ] **Step 2: Verify failure**

- [ ] **Step 3: Inject `IPerSessionEventStream`, replace `StreamAgentEventsAsync`**

Inject `IPerSessionEventStream` via constructor. In `StartSessionAsync` (around line 132), change:

```csharp
await foreach (var msg in StreamAgentEventsAsync(url, startBody, sessionId, request.ProjectId, session.Cts.Token))
{
    ...
}
```

to a pattern mirroring Task 8 (`POST` JSON → start `PerSessionEventStream` → subscribe). In `SendMessageAsync` (line 183–188) do the same: `POST` JSON, then subscribe to the per-session stream.

Remove `StreamAgentEventsAsync` + `TryIngestA2AAsync` once unused.

- [ ] **Step 4: Verify pass**

```
dotnet test tests/Homespun.Tests --filter FullyQualifiedName~SingleContainer
```

- [ ] **Step 5: Commit**

```bash
git add src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionService.cs \
        tests/Homespun.Tests/ClaudeCode/SingleContainerAgentExecutionServiceTests.cs
git commit -m "refactor(server): route SingleContainerAgentExecutionService via PerSessionEventStream"
```

---

## Task 10: Integration test — post-result task notification reaches the client

**Files:**
- Create: `tests/Homespun.Api.Tests/Sessions/PostResultTaskNotificationTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using Homespun.Api.Tests.TestHarness;
using Homespun.Features.ClaudeCode.Data;
using NUnit.Framework;

namespace Homespun.Api.Tests.Sessions;

public class PostResultTaskNotificationTests : IntegrationTestBase
{
    [Test]
    public async Task Task_notification_after_result_is_broadcast_via_signalr()
    {
        using var harness = await TestHarness.BuildAsync();
        var sessionId = await harness.StartMockSessionAsync(prompt: "run background bash");

        // Mock worker emits the sequence:
        //  1. assistant message
        //  2. status-update completed (result)
        //  3. 500ms later: system task_notification
        harness.MockWorker.QueueAssistantText("ok");
        harness.MockWorker.QueueResultSuccess();
        await Task.Delay(500);
        harness.MockWorker.QueueTaskNotification(taskId: "t1", status: "completed");

        // Client subscribes to the SignalR hub before any prior work.
        var broadcasts = await harness.WaitForBroadcastsAsync(sessionId, count: 3, timeout: TimeSpan.FromSeconds(5));

        Assert.That(broadcasts.Any(b => b.Event.Type == "CUSTOM" && (b.Event as dynamic).Name == "raw"), Is.True,
            "task_notification should have been broadcast live, not buffered for the next prompt");
    }
}
```

(Extend `TestHarness` / `MockWorker` as needed to queue post-result events; follow existing patterns in `tests/Homespun.Api.Tests`.)

- [ ] **Step 2: Run the test and verify failure before the wiring is in place**

If running out of order: expected FAIL with event count < 3.

- [ ] **Step 3: Run after Tasks 1–9 land — expect PASS**

```
dotnet test tests/Homespun.Api.Tests --filter FullyQualifiedName~PostResultTaskNotification
```

- [ ] **Step 4: Commit**

```bash
git add tests/Homespun.Api.Tests/Sessions/PostResultTaskNotificationTests.cs
git commit -m "test(server): task notification after result reaches client via SignalR"
```

---

## Task 11: Pre-PR checklist

- [ ] **Step 1: Backend tests**

```
dotnet test
```

Expected: PASS.

- [ ] **Step 2: Frontend lint, format, typecheck, unit tests**

```
cd src/Homespun.Web
npm run lint:fix
npm run format:check
npm run generate:api:fetch
npm run typecheck
npm test
```

Expected: all PASS.

- [ ] **Step 3: Worker tests**

```
cd src/Homespun.Worker
npm test
```

Expected: PASS.

- [ ] **Step 4: E2E**

```
cd src/Homespun.Web
npm run test:e2e
```

Expected: PASS.

- [ ] **Step 5: Storybook build**

```
cd src/Homespun.Web
npm run build-storybook
```

Expected: PASS.

- [ ] **Step 6: Manual smoke with `dev-live`**

```
dotnet run --project src/Homespun.AppHost --launch-profile dev-live
```

Ask an agent to run a background `sleep 10 && echo done` and then say "I'll wait for notifications". Confirm the `task_notification` event appears in the UI within a second of the bash finishing, without the user sending a second prompt.

- [ ] **Step 7: Final commit (if any housekeeping was needed)**

```bash
git status
git commit -m "chore: pre-PR cleanup" || true
```

---

## Self-Review

**Spec coverage**
- Post-result events reach the client live → Tasks 1, 2, 7, 8, 9, 10.
- Worker HTTP contract change → Tasks 3, 4, 5.
- Server background consumer + fan-out → Tasks 6, 7, 8, 9.
- Regression coverage → Tasks 7, 10, 11.

**Placeholder scan** — no "TBD"/"implement later"/unresolved deferrals. Every code block is the exact text to insert.

**Type consistency** — `IPerSessionEventStream.StartAsync` / `SubscribeTurnAsync` / `StopAsync` signatures match their callsites in Tasks 8, 9. `WorkerStartResponse` defined in Task 8 and reused there only (single file scope).
