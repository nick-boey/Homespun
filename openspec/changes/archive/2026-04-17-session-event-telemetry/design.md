## Context

The A2A-native session event pipeline passes six hops between the Claude Agent SDK and the client reducer. Today each tier logs in its own style: the worker emits pino-shaped JSON via `console.log` with fields `{Timestamp, Level, Message, SourceContext, Component, IssueId, ProjectName}`; the server uses Serilog with `SourceContext`/`Component`; the client uses plain `console.log` that never reaches Loki. When a message fails to appear in the UI, there is no way to answer "which hop dropped it?" from Grafana.

The A2A protocol spec (https://a2a-protocol.org/latest/topics/enterprise-ready) recommends OpenTelemetry with W3C Trace Context propagation and mandates that logs include `taskId` and `contextId`. A2A events carry first-class correlation IDs:

| A2A event type | Required IDs | Optional IDs |
|---|---|---|
| `Message` | `messageId` (UUID, creator-assigned), `role`, `parts` | `contextId`, `taskId`, `referenceTaskIds[]`, `metadata` |
| `Task` | `id`, `contextId`, `status` | `history`, `artifacts`, `metadata` |
| `TaskStatusUpdateEvent` | `taskId`, `contextId`, `status` | `final`, `metadata` — **no event id**, keyed by `(taskId, status.timestamp)` |
| `TaskArtifactUpdateEvent` | `taskId`, `contextId`, `artifact.artifactId` | `append`, `lastChunk`, `metadata` |

Homespun's current mapping sets `contextId = Homespun sessionId` and `taskId = sessionId` (1:1). The worker stamps `messageId` via `randomUUID()` at `createMessage()`.

Separately, `DockerAgentExecutionService` spawns one worker container per issue via Docker-outside-of-Docker. That pattern requires `/var/run/docker.sock` mounted into the server container. On Windows from host `dotnet run`, there is no such socket and the spawn fails, preventing live-session debugging.

## Goals / Non-Goals

**Goals:**
- One LogQL query pinned to a single `messageId` or `sessionId` returns the full six-hop chain from worker SDK-receive to client reducer-apply.
- Zero behavioural change to the A2A → AG-UI translator, the event store, or the SignalR broadcast pipeline. Logging is strictly observational.
- Client logs reach Loki via a single HTTP endpoint; the browser console remains readable.
- Production log volume is controlled by a single configuration key (`SessionEventLog:ContentPreviewChars`) with sensible defaults.
- Windows developers can run live session debugging via `./scripts/mock.sh --with-worker` and reproduce the "where did the message go" symptom against a real Claude Agent SDK.

**Non-Goals:**
- Fixing the eventId dedup collision in `use-session-events.ts:103` — this change provides the instrument, not the fix.
- Redesigning the thinking-bubble UX — separate change, orthogonal.
- Rendering `RunFinished.result` — explicitly not rendered; the assistant's own text block carries the visible content.
- Adding OpenTelemetry or W3C Trace Context propagation — possibly in a follow-up; overkill for single-user dev-tool debugging.
- Multi-tenant worker execution for `SingleContainer` mode — explicitly single-active-session only.

## Decisions

### 1. Shared JSON field schema across all three tiers

Every `SessionEventLog` entry carries the following fields, flat, in a single JSON object:

```
{
  "Timestamp": "2026-04-17T10:15:32.345Z",
  "Level": "Information",
  "SourceContext": "Homespun.SessionEvents" | "Homespun.ClientSessionEvents" | "Worker",
  "Component": "Server" | "Web" | "Worker",
  "Message": "server.signalr.tx seq=42 aguiType=TEXT_MESSAGE_CONTENT msg=a1b2",
  "Hop": "worker.a2a.emit" | "server.sse.rx" | "server.ingest.append"
       | "server.agui.translate" | "server.signalr.tx"
       | "client.signalr.rx" | "client.reducer.apply",
  "SessionId": "<Homespun sessionId = A2A contextId>",
  "TaskId": "<A2A taskId>",
  "MessageId": "<A2A messageId; omitted for non-Message events>",
  "ArtifactId": "<A2A artifact.artifactId; omitted for non-artifact events>",
  "StatusTimestamp": "<ISO 8601; omitted for non-StatusUpdate events>",
  "A2AKind": "message" | "task" | "status-update" | "artifact-update",
  "AGUIType": "TEXT_MESSAGE_START" | ... | "CUSTOM",
  "AGUICustomName": "thinking" | "system.init" | ...,
  "Seq": 42,
  "EventId": "<server-assigned UUID; present from server.ingest.append onward>",
  "ContentPreview": "<truncated per config; omitted when ContentPreviewChars = 0>"
}
```

- `Message` is a human-readable summary for browser console and `tail -f` ergonomics. All structured fields are top-level, not embedded in `Message`.
- `Hop` is the single-field filter Grafana users will pin on.
- `MessageId`/`ArtifactId`/`StatusTimestamp` are mutually exclusive; whichever applies to the event type is emitted. For AG-UI hops where a parent A2A event generated several AG-UI envelopes, all AG-UI log lines share the parent `MessageId` (or status correlation) plus the server-assigned `EventId`.

**Alternatives considered:**
- Embedding structured fields inside `Message` as key-value pairs: rejected, because Loki's `| json` pipeline stage is the natural filter and flat top-level fields are cheapest to index.
- Using OpenTelemetry span IDs: overkill for a single-process worker and single-service server; adds build-graph complexity without a matching benefit at this scale.

### 2. `/api/log/client` as the client → Loki bridge

The React client posts batches of log entries to `POST /api/log/client`. The server controller accepts up to 100 entries per request (413 otherwise), validates the JSON schema, and forwards each to Serilog at the logged `Level` under `SourceContext = "Homespun.ClientSessionEvents"`.

Client batcher (in `src/Homespun.Web/src/lib/session-event-log.ts`):
- Buffer entries in memory.
- Flush on whichever comes first: 50 entries OR 500 ms since the oldest buffered entry.
- Flush on `window.beforeunload` with a best-effort `navigator.sendBeacon` fall-through so the last batch reaches Loki on nav.
- On HTTP failure: drop the batch, log ONE `console.warn` per flush failure, and do NOT re-queue. This prevents a feedback loop when the server is down.
- The batcher NEVER calls `sessionEventLog()` for its own failures — only `console.warn`.

**Alternatives considered:**
- WebSocket over SignalR for client logs: rejected as over-engineered; HTTP batching is enough and cheaper to reason about.
- `sendBeacon` as the primary transport: rejected because `sendBeacon` requires `Content-Type` restrictions (`application/json` is allowed but body size limits apply) and does not support response inspection for backpressure. We use it only on unload.

### 3. Content preview is the one volume knob

`SessionEventLog:ContentPreviewChars` (server) and `VITE_SESSION_EVENT_CONTENT_PREVIEW_CHARS` (client, read at build time and optionally runtime-overridable via a signed cookie) govern truncation length. `0` disables the `ContentPreview` field entirely. Defaults:

- Development: `80`
- Production: `0`
- `appsettings.Development.json` seeds the dev default; Program.cs enforces the prod default if Environment is Production and the setting is unset.

Individual hops can be further disabled via `SessionEventLog:Hops:<hopName>:Enabled = false` to handle the rare "this hop is noisy in staging" case. Default is enabled.

**Trade-off:** a deployment that sets `ContentPreviewChars` in Production will ship preview text to Loki. This is intentional — someone who turned it on made that choice. Validated by a startup warning when `ContentPreviewChars > 0 && IsProduction()`.

### 4. `SingleContainerAgentExecutionService` is a hard-gated dev-only shim

A new `IAgentExecutionService` implementation that:
- Resolves its worker URL from `AgentExecution:SingleContainer:WorkerUrl` (required, startup validation).
- Shares a single `HttpClient` (timeout = same `RequestTimeout` as Docker mode; default 30 min).
- Tracks a single `_currentSession : (sessionId, cts)` field, guarded by a `SemaphoreSlim(1, 1)` for the short critical section around "is busy?" check + set.
- Throws `SingleContainerBusyException(requestedSessionId, currentSessionId)` when a second `StartSessionAsync` arrives while `_currentSession != null`.
- Caller (`MessageProcessingService` or `SessionLifecycleService`) catches the exception, transitions the session to `Error` with a user-friendly message, and the existing `BroadcastSessionError` path surfaces the SignalR toast. No new notification plumbing required.
- `StopSessionAsync(sessionId)` clears `_currentSession` when it matches.

Registration (in `Program.cs`):

```csharp
var agentMode = builder.Configuration["AgentExecution:Mode"] ?? "Docker";
if (agentMode == "SingleContainer")
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AgentExecution:Mode=SingleContainer is only permitted in Development.");
    }
    builder.Services.Configure<SingleContainerAgentExecutionOptions>(
        builder.Configuration.GetSection(SingleContainerAgentExecutionOptions.SectionName));
    builder.Services.AddSingleton<IAgentExecutionService, SingleContainerAgentExecutionService>();
}
else
{
    // existing Docker registration
}
```

**Alternatives considered:**
- A runtime feature flag surfaced in the UI: rejected; adds attack surface. The env var gate is enough for a dev-only tool.
- Supporting multiple concurrent sessions by proxying per-session routes: rejected; the compose worker's `SessionManager` already supports this, but doing so here would mask concurrency bugs the real Docker path isolates via per-session containers. Single-session enforcement keeps the shim honestly narrower than production.

### 5. `mock.sh --with-worker` and compose port exposure

Compose `docker-compose.yml` gets `ports: "${WORKER_HOST_PORT:-8081}:8080"` added to the `worker` service. Unset in production envs because production doesn't use compose.

`mock.sh` / `mock.ps1` behaviour with `--with-worker`:
1. Check `CLAUDE_CODE_OAUTH_TOKEN` is set. If not, print a warning and exit non-zero.
2. Run `docker compose up -d worker` (Linux/WSL) or the Windows equivalent.
3. Poll the worker's `/api/health` endpoint (on `http://localhost:${WORKER_HOST_PORT}/api/health`) up to 30 s.
4. Export `AgentExecution__Mode=SingleContainer` and `AgentExecution__SingleContainer__WorkerUrl=http://localhost:${WORKER_HOST_PORT}`.
5. Chain into the existing mock server startup.
6. Register a trap to `docker compose stop worker` on exit.

Without `--with-worker`, existing `mock.sh` behaviour is unchanged (mock agent execution).

## Risks / Trade-offs

- [Log volume explodes in Production if `ContentPreviewChars > 0`] → mitigated by default=`0` in Production, a startup warning if set otherwise, and per-hop `Enabled` flags as an escape hatch.
- [Client logs create a feedback loop if the client hits `/api/log/client` during an outage] → mitigated by the self-defensive batcher (no retries on HTTP failure, no structured-log routing of its own errors) and a `413 Payload Too Large` from the server if a batch is malformed.
- [Single-container mode confuses multi-developer dev environments] → mitigated by `IsDevelopment()` gate and the busy-exception toast. Documented as a single-developer assumption in the `mock.sh` README block.
- [Windows `docker-compose` paths don't match Windows filesystem] → documented. The live sessions operate on the compose worker's Linux view; `Read`/`Write`/`Bash` tool calls target `/workdir` inside the container, not the Windows host. Acceptable for the narrow "debug the pipeline" use case.
- [A future A2A spec revision changes correlation field shapes] → the logger helpers take a discriminated union of A2A event types and extract IDs from them; any rename would be a single-file change.
- [`TaskStatusUpdateEvent` has no event id] → we log `StatusTimestamp` as the per-status correlation, plus the server-assigned `EventId` from the append step. Two status updates with identical timestamps would share correlation; this is a theoretical risk that the spec itself leaves open, and the server's `Seq` disambiguates in practice.
- [`IsDevelopment()` bypassed by `ASPNETCORE_ENVIRONMENT=Development` in a prod deployment] → at registration time we also check for the absence of typical prod env markers, but ultimately this is an environment-configuration discipline problem. The startup throw ensures at least that `AgentExecution__Mode=SingleContainer` alone isn't enough — both must agree.

## Migration Plan

No data migration. The change is additive:
1. Deploy server with logging code paths behind `ContentPreviewChars = 0` default in Production — zero preview text leaks.
2. Client code batcher is a no-op until the server's `/api/log/client` endpoint responds; client never crashes on a 404.
3. `SingleContainer` mode is off unless explicitly enabled.

Rollback: revert the PR; no state to unwind. The `A2AEventStore.jsonl` format is unchanged.

## Open Questions

- Should the server-side client-log endpoint enforce an authenticated session to prevent a malicious client from flooding it? For v1 we rely on the existing CORS / auth that protects the rest of the API; if auth is planned before production, this endpoint inherits it. Revisit if the endpoint becomes reachable unauthenticated.
- Should `ContentPreview` redact known-sensitive fields (e.g. `CLAUDE_CODE_OAUTH_TOKEN` patterns)? Out of scope for v1. Revisit if previews ever turn on in Production.
