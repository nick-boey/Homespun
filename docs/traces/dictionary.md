# Trace Dictionary

Single source of truth for every span emitted by Homespun. Every tracer /
ActivitySource name in the codebase must appear in the registry below; every
statically-named span in source must have an H3 entry under one of the three
`*-originated traces` sections for its tier. Drift is enforced by:

- `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs`
- `src/Homespun.Web/src/test/trace-dictionary.test.ts`
- `tests/Homespun.Worker/observability/trace-dictionary.test.ts`

Add / rename / remove a span → update this file in the **same PR**. See
[README.md](./README.md) for the workflow.

## Conventions

- Span names: `snake.dot.case` lowercase, scoped by emitter prefix (`homespun.`,
  `signalr.`, `http.`). `SignalR.<Hub>/<Method>` is retained in PascalCase
  because it is the canonical form the server's `TraceparentHubFilter` emits —
  see the allowlist note under [Drift check](#drift-check).
- Attribute keys under `homespun.*` are Homespun-specific. Prefer stable
  OTel semantic conventions (`http.*`, `url.*`, `exception.*`, `gen_ai.*`,
  `service.*`, `deployment.*`) where they apply.
- Every H3 span entry names: **originator** (client / server / worker), **kind**
  (`SERVER` / `CLIENT` / `INTERNAL` / `PRODUCER` / `CONSUMER`), **required
  attrs**, and optionally: parent, optional attrs, child spans, span events,
  recorded exceptions.

## Tracer / ActivitySource registry

Every name in this table corresponds to at least one `ActivitySource(name)` or
`trace.getTracer(name)` / `logs.getLogger(name)` call site. The drift check
asserts that the reverse also holds.

| Name                                    | Tier         | Kind          | Notes                                                    |
|-----------------------------------------|--------------|---------------|----------------------------------------------------------|
| `Microsoft.AspNetCore.Hosting`          | server       | auto (.NET)   | `http.server.request` — stable OTel HTTP semconv         |
| `System.Net.Http`                       | server       | auto (.NET)   | `http.client.request` — stable OTel HTTP semconv         |
| `Homespun.AgentOrchestration`           | server       | ActivitySource| Session + agent lifecycle (reserved — see Planned)       |
| `Homespun.GitClone`                     | server       | ActivitySource| Clone / worktree ops (reserved — see Planned)            |
| `Homespun.FleeceSync`                   | server       | ActivitySource| Fleece issue sync (reserved — see Planned)               |
| `Homespun.Signalr`                      | server       | ActivitySource| `TraceparentHubFilter` hub-invocation + `ClaudeCodeHub` connect/join/leave spans |
| `Homespun.SessionPipeline`              | server       | ActivitySource| A2A ingest + AG-UI translate spans                       |
| `homespun.web`                          | client       | Tracer+Logger | Root React app tracer and logger                         |
| `homespun.web.signalr`                  | client       | Tracer        | SignalR client-span wrapper + lifecycle span             |
| `homespun.web.session-events`           | client       | Tracer        | Envelope rx + reducer-apply spans                        |
| `homespun.worker`                       | worker       | Logger        | Hono worker logger (spans deferred — see Planned)        |
| `homespun.worker.http`                  | worker       | Tracer        | Hono inbound-request SERVER spans from `traceparentMiddleware` (parent comes from the caller's `traceparent` header) |

---

## Client-originated traces

### `homespun.signalr.client.connect`

Long-lived span covering a SignalR hub connection's lifetime. Emitted once per
connection from `src/Homespun.Web/src/providers/signalr-provider.tsx` on the
`homespun.web.signalr` tracer; lifecycle status transitions are attached as
span events.

- **Originator:** client
- **Kind:** `INTERNAL`
- **Events:** `signalr.<status>` (e.g. `signalr.connected`,
  `signalr.reconnecting`, `signalr.disconnected`) with attrs
  `homespun.signalr.status`, and `exception.message` when the transition
  carried an error.

### `signalr.invoke.<method>`

Client-side SignalR hub invocation. Emitted by `traceInvoke` in
`src/Homespun.Web/src/lib/signalr/trace.ts` on the `homespun.web.signalr`
tracer. Dynamic span-name suffix — see [Drift check](#drift-check) allowlist.

- **Originator:** client
- **Kind:** `CLIENT`
- **Propagation:** `injectTraceparent()` is prepended as wire arg 0; the
  server's `TraceparentHubFilter` extracts it and parents its
  `SignalR.<Hub>/<Method>` span to this one.
- **Records exceptions:** any invoke error is captured via
  `span.recordException` and the status set to `ERROR`.

### `homespun.envelope.rx`

Client receives a `ReceiveSessionEvent` envelope broadcast. Emitted from
`src/Homespun.Web/src/features/sessions/hooks/use-session-events.ts` on the
`homespun.web.session-events` tracer inside
`withExtractedContext(envelope, …)`, so when the envelope carries a
`Traceparent` this span parents to the server's broadcast.

- **Originator:** client
- **Kind:** `CONSUMER`
- **Parent:** extracted from `SessionEventEnvelope.Traceparent` when present;
  root span otherwise (e.g. replay).
- **Required attrs:** `homespun.session.id`, `homespun.event.id`,
  `homespun.event.seq`, `homespun.agui.type`.
- **Child spans:** `homespun.client.reducer.apply`.

### `homespun.client.reducer.apply`

Reducer-side application of a single envelope to the per-session AG-UI state.
Same emitter file as `homespun.envelope.rx`; runs inside `applyOne` under the
same extracted context so both spans join the server trace.

- **Originator:** client
- **Kind:** `CONSUMER`
- **Parent:** extracted from envelope `Traceparent`; typically sibling of
  `homespun.envelope.rx` under the same server-ingest parent.
- **Required attrs:** `homespun.session.id`, `homespun.event.id`,
  `homespun.event.seq`, `homespun.agui.type`, `homespun.agui.custom.name`
  (empty string when `event.type !== 'CUSTOM'`).

---

## Server-originated traces

### `http.server.request`

ASP.NET Core auto-instrumentation via `AddAspNetCoreInstrumentation()` in
`Homespun.ServiceDefaults/Extensions.cs`. Not emitted by Homespun code; the
entry is here so operators querying Seq / the Aspire dashboard can find it.

- **Originator:** server (auto — `Microsoft.AspNetCore.Hosting`)
- **Kind:** `SERVER`
- **Attrs:** stable `http.*`, `url.*`, `server.*` per OTel semconv.
- **Parent:** extracted from inbound `traceparent` header (W3C Trace Context).

### `http.client.request`

`HttpClient` auto-instrumentation via `AddHttpClientInstrumentation()`. Covers
worker proxy calls, GitHub Octokit calls, and any outbound HTTP.

- **Originator:** server (auto — `System.Net.Http`)
- **Kind:** `CLIENT`
- **Attrs:** stable `http.*`, `url.*`, `server.*` per OTel semconv.

### `SignalR.<Hub>/<Method>`

Hub method invocation span. Emitted by
`src/Homespun.Server/Features/Observability/TraceparentHubFilter.cs` on the
`Homespun.Signalr` ActivitySource. Dynamic span name (`$"SignalR.{hubName}/
{methodName}"`) — see [Drift check](#drift-check) allowlist.

The native `Microsoft.AspNetCore.SignalR.Server` source is intentionally
**not** registered on the tracer provider so this filter owns the span tree
and no double-emission occurs (see comment in `Extensions.cs`).

- **Originator:** server
- **Kind:** `SERVER`
- **Parent:** `ActivityContext` parsed from wire arg 0 (the client's
  `traceparent`) when present; root when absent or malformed.
- **Required attrs:** `homespun.signalr.hub`, `homespun.signalr.method`.
- **Optional attrs:** `homespun.session.id` — set when wire arg 1 is a
  string (convention: sessionId follows traceparent on most hub methods).
- **Records exceptions:** rethrown exceptions from the downstream method are
  attached via `AddException` and the status is set to `Error`.

### `homespun.session.ingest`

Server-side A2A ingest span. Emitted by
`src/Homespun.Server/Features/ClaudeCode/Services/SessionEventIngestor.cs`
on the `Homespun.SessionPipeline` ActivitySource — one per A2A event
received from a worker. Carries the append-before-broadcast pipeline as
ordered span events.

- **Originator:** server
- **Kind:** `CONSUMER`
- **Parent:** the inbound worker SSE `http.server.request` span.
- **Required attrs:** `homespun.session.id`, `homespun.a2a.kind`,
  `homespun.seq`, `homespun.event.id`.
- **Optional attrs:** `homespun.task.id`, `homespun.message.id`,
  `homespun.artifact.id`, `homespun.content.preview` (gated by
  `SessionEventContent:ContentPreviewChars`).
- **Events:** `sse.rx` (before parse), `ingest.append` (after
  `A2AEventStore.AppendAsync`), `signalr.tx` (after the envelope broadcast).
- **Child spans:** `homespun.agui.translate`.
- **Records exceptions:** broadcast failure is attached via `AddException`
  and the status set to `Error`; the append is not reversed.

### `homespun.agui.translate`

Child of `homespun.session.ingest`. Covers the pure A2A → AG-UI translator
call. Same emitter file as the parent span.

- **Originator:** server
- **Kind:** `INTERNAL`
- **Parent:** `homespun.session.ingest`.
- **Required attrs:** `homespun.session.id`, `homespun.a2a.kind`.

### `homespun.signalr.connect`

Long-lived hub-connection span. Emitted by
`src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs`
`OnConnectedAsync`; stopped in `OnDisconnectedAsync`. One span per SignalR
connection, held across the connection's lifetime in `Context.Items`.

- **Originator:** server
- **Kind:** `SERVER`
- **Required attrs:** `signalr.connection.id`.
- **Events:** `connected` (at start), `disconnected` (at end; carries a
  `reason` tag when the disconnect exception is non-null).
- **Records exceptions:** disconnect exceptions are attached via
  `AddException` and the status set to `Error`.

### `homespun.signalr.join`

Discrete join span. Emitted by
`src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs`
`JoinSession`.

- **Originator:** server
- **Kind:** `SERVER`
- **Parent:** the matching `SignalR.ClaudeCodeHub/JoinSession` span started
  by `TraceparentHubFilter`.
- **Required attrs:** `homespun.session.id`, `signalr.connection.id`.

### `homespun.signalr.leave`

Discrete leave span. Emitted by
`src/Homespun.Server/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs`
`LeaveSession`.

- **Originator:** server
- **Kind:** `SERVER`
- **Parent:** the matching `SignalR.ClaudeCodeHub/LeaveSession` span started
  by `TraceparentHubFilter`.
- **Required attrs:** `homespun.session.id`, `signalr.connection.id`.

---

## Worker-originated traces

### `homespun.a2a.emit`

Emitted by `src/Homespun.Worker/src/services/sse-writer.ts` on the
`homespun.worker` tracer — one per A2A event written to the SSE response
stream. Represents the worker → server handoff.

- **Originator:** worker
- **Kind:** `PRODUCER`
- **Parent:** the active Hono request span (auto-instrumentation).
- **Required attrs:** `homespun.session.id`, `homespun.a2a.kind`.
- **Optional attrs:** `homespun.task.id`, `homespun.message.id`,
  `homespun.artifact.id`, `homespun.status.timestamp`, `homespun.agui.type`,
  `homespun.agui.custom_name`, `homespun.seq`, `homespun.event.id`,
  `homespun.content.preview` (gated by `CONTENT_PREVIEW_CHARS` env).

---

## Planned / reserved

Entries here document ActivitySources / logger names that are registered
today but do not yet emit spans with pinned names. **The drift check ignores
this section** — it is reference, not contract. Promote an entry up to one of
the tier sections in the same PR that wires its span call site.

### `Homespun.AgentOrchestration` (reserved)

Owner: `IClaudeSessionService` / `DockerAgentExecutionService`. Planned
representative spans:

- `homespun.session.start` — SERVER kind, covers
  `StartSessionWithTermination`. Required attrs: `homespun.session.id`,
  `homespun.project.id`, `homespun.agent.mode`.
- `homespun.docker.spawn` — CLIENT kind, Docker sibling-container spawn via
  DooD. Required attrs: `homespun.container.name`,
  `homespun.container.image`. Records exceptions:
  `AgentStartupException`.

### `Homespun.GitClone` (reserved)

Owner: `GitCloneService`. Planned representative span:

- `homespun.git.clone` — CLIENT kind. Required attrs:
  `git.repository.url`, `git.branch`.

### `Homespun.FleeceSync` (reserved)

Owner: `FleeceIssuesSyncService`. Planned representative span:

- `homespun.fleece.sync` — INTERNAL kind, once per project tick. Required
  attrs: `homespun.project.id`. Events: `issues.added`, `issues.removed`.

### `homespun.worker` (reserved spans)

Planned span surface covered by the sibling `worker-spans` change:

- `homespun.worker.session.init` — SERVER kind.
- `homespun.claude.query` — CLIENT kind, carries `gen_ai.*` attrs.

---

## Drift check

Each tier's test suite parses this file and compares against ActivitySource /
tracer / logger emissions in that tier's source tree.

- **What the parser collects:**
  - Registry names from the `Tracer / ActivitySource registry` table (column
    one, backtick-quoted values).
  - H3 headings whose text starts with `` `homespun. ``, `` `signalr. ``,
    `` `SignalR. ``, or `` `http. `` — only those directly under the three
    `*-originated traces` H2 sections. Everything under `Planned / reserved`
    is ignored.
- **What the drift check asserts:**
  - Every ActivitySource / tracer / logger name in code appears in the
    registry table (direction: code → doc).
  - Every statically-named span used in code appears as an H3 entry in the
    corresponding tier section (direction: code → doc).
  - Every H3 entry in a tier section is emitted by at least one code path
    in that tier (direction: doc → code — orphan detection).
- **Allowlisted dynamic span names** (interpolated at emit time, cannot be
  matched statically):
  - `SignalR.<Hub>/<Method>` — server, emitted by `TraceparentHubFilter`.
  - `signalr.invoke.<method>` — client, emitted by `traceInvoke`.
  Each entry in the tier test's allowlist carries a comment naming the
  emitter file + line.
