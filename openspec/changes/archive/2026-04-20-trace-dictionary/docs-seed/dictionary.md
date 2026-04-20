# Trace Dictionary

Single source of truth for every span emitted by Homespun. Every tracer /
ActivitySource name in the codebase must appear here. Drift is enforced by:

- `tests/Homespun.Tests/Observability/TraceDictionaryTests.cs`
- `src/Homespun.Web/src/test/trace-dictionary.test.ts`
- `src/Homespun.Worker/src/test/trace-dictionary.test.ts`

## Conventions

- Span names: `snake.dot.case` lowercase, scoped by emitter prefix.
- Attribute keys under `homespun.*` are Homespun-specific. Prefer stable OTel
  semantic conventions (`http.*`, `url.*`, `exception.*`, `gen_ai.*`,
  `service.*`, `deployment.*`) where they apply.
- Every span documents: originator, trace roots it can appear under,
  required attributes, optional attributes, child spans, span events.
- Span kinds: `SERVER`, `CLIENT`, `INTERNAL`, `PRODUCER`, `CONSUMER`.

## Tracer / ActivitySource registry

| Name                                 | Emitter      | Notes                         |
|--------------------------------------|--------------|-------------------------------|
| `Microsoft.AspNetCore.Hosting`       | .NET native  | http.server request spans     |
| `System.Net.Http`                    | .NET native  | http.client spans             |
| `Homespun.AgentOrchestration`        | server       | session lifecycle             |
| `Homespun.GitClone`                  | server       | clone ops                     |
| `Homespun.FleeceSync`                | server       | issue sync                    |
| `Homespun.SessionPipeline`           | server       | A2A → AG-UI ingest            |
| `Homespun.Signalr`                   | server       | hub filter + lifecycle        |
| `homespun.web`                       | client       | React app tracer              |
| `homespun.worker`                    | worker       | Hono worker tracer            |

---

## Client-originated traces

### `homespun.user.action.<name>`

Root span for most user-initiated flows.

- **Originator:** client (React — button click, form submit, route action)
- **Kind:** `INTERNAL`
- **Required attrs:** `homespun.action.name`, `homespun.route.path`
- **Optional attrs:** `homespun.entity.type`, `homespun.entity.id`
- **Child spans:** `http.client.request` (fetch auto-instr), `signalr.invoke.<method>`

### `signalr.invoke.<method>`

Client-side SignalR hub invocation.

- **Originator:** client (signalr-provider wrapper)
- **Kind:** `CLIENT`
- **Required attrs:** `signalr.hub`, `signalr.method`
- **Optional attrs:** `homespun.session.id`
- **Propagation:** traceparent passed as first method arg (TraceparentHubFilter extracts server-side)

### `homespun.navigation`

Route change via TanStack Router.

- **Originator:** client
- **Kind:** `INTERNAL`
- **Required attrs:** `http.route`, `url.path`

### `homespun.envelope.rx`

Client receives a SignalR envelope broadcast.

- **Originator:** client
- **Kind:** `CONSUMER`
- **Parent:** extracted from envelope `Traceparent`
- **Required attrs:** `homespun.session.id`, `homespun.agui.type`
- **Optional attrs:** `homespun.agui.custom_name`
- **Events:** `reducer.dispatched`

### `homespun.signalr.client.connect`

Long-lived span covering a hub connection's lifetime.

- **Originator:** client
- **Kind:** `CLIENT`
- **Required attrs:** `signalr.hub`
- **Events:** `reconnecting`, `reconnected`, `disconnected`

---

## Server-originated traces

### `http.server.request`

ASP.NET auto-instrumentation.

- **Originator:** server (auto)
- **Attrs:** stable `http.*`, `url.*`, `server.*`
- **Parent:** extracted from inbound `traceparent` header

### `SignalR.<hub>/<method>`

Hub method invocation. Emitted by `Homespun.Signalr` via `TraceparentHubFilter`.

- **Originator:** server
- **Kind:** `SERVER`
- **Parent:** extracted from traceparent first-arg
- **Enriched attrs:** `homespun.session.id` (when second arg is a sessionId string)

### `homespun.session.start`

End-to-end session creation.

- **Originator:** server (`IClaudeSessionService.StartSessionWithTermination`)
- **Kind:** `INTERNAL`
- **Required attrs:** `homespun.session.id`, `homespun.project.id`, `homespun.agent.mode`
- **Child spans:** `homespun.docker.spawn`, `homespun.worker.session.init`
- **Events:** `container.reused`, `container.created`

### `homespun.docker.spawn`

Docker sibling-container spawn via DooD.

- **Originator:** server (`DockerAgentExecutionService.RunDockerAndGetUrl`)
- **Kind:** `CLIENT`
- **Required attrs:** `homespun.container.name`, `homespun.container.image`
- **Events:** `docker.run.exit`, `docker.inspect.exit`
- **Records exceptions:** `AgentStartupException`

### `homespun.session.ingest`

Server ingests one worker-emitted A2A event and broadcasts an AG-UI envelope.

- **Originator:** server (`SessionEventIngestor`)
- **Kind:** `CONSUMER`
- **Parent:** inbound SSE span
- **Required attrs:** `homespun.session.id`, `homespun.a2a.kind`, `homespun.seq`
- **Optional attrs:** `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`, `homespun.content.preview`
- **Child spans:** `homespun.agui.translate`
- **Events:** `sse.rx`, `ingest.append`, `signalr.tx`

### `homespun.agui.translate`

Translates A2A event to AG-UI event.

- **Originator:** server (`A2AToAGUITranslator`)
- **Kind:** `INTERNAL`
- **Required attrs:** `homespun.a2a.kind`, `homespun.agui.type`
- **Optional attrs:** `homespun.agui.custom_name`

### `homespun.signalr.join` / `homespun.signalr.leave`

Hub group membership.

- **Originator:** server
- **Kind:** `SERVER`
- **Required attrs:** `homespun.session.id`, `signalr.connection.id`

### `homespun.github.sync.polling`

Background PR-sync tick.

- **Originator:** server (`GitHubSyncPollingService`)
- **Kind:** `INTERNAL`
- **Events:** `prs.scanned` (count), `reviews.polled`

### `homespun.fleece.sync`

Fleece issue sync per project.

- **Originator:** server (`FleeceIssuesSyncService`)
- **Kind:** `INTERNAL`
- **Required attrs:** `homespun.project.id`
- **Events:** `issues.added` (count), `issues.removed` (count)

### `homespun.git.clone`

Git clone + worktree prep.

- **Originator:** server (`GitCloneService`)
- **Kind:** `CLIENT`
- **Required attrs:** `git.repository.url`, `git.branch`

### `homespun.mini_prompt.complete`

Mini-prompt sidecar call.

- **Originator:** server (`MiniPromptService`)
- **Kind:** `CLIENT`
- **Required attrs:** `homespun.mini_prompt.purpose`
- **`gen_ai.*` attrs:** `gen_ai.system = claude`, `gen_ai.request.model`,
  `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`

---

## Worker-originated traces

### `homespun.worker.session.init`

Worker initialises a new Claude SDK session.

- **Originator:** worker (`session-manager.ts`)
- **Kind:** `SERVER`
- **Parent:** inbound HTTP request from server
- **Required attrs:** `homespun.session.id`, `homespun.worker.session_id`
- **Child spans:** `homespun.claude.query`

### `homespun.claude.query`

One Claude SDK Query iterable.

- **Originator:** worker
- **Kind:** `CLIENT`
- **Required attrs:** `gen_ai.system = claude`, `gen_ai.request.model`
- **Optional attrs:** `gen_ai.request.max_tokens`, `gen_ai.request.temperature`,
  `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.response.finish_reasons`
- **Child spans:** `homespun.a2a.emit` (×N)
- **Records exceptions:** SDK errors

### `homespun.a2a.emit`

Worker emits an A2A event upstream.

- **Originator:** worker
- **Kind:** `PRODUCER`
- **Required attrs:** `homespun.session.id`, `homespun.a2a.kind`
- **Optional attrs:** `homespun.task.id`, `homespun.message.id`, `homespun.artifact.id`, `homespun.content.preview`

### `homespun.worker.skills.discover`

Skills directory enumeration at worker boot.

- **Originator:** worker (`openspec-snapshot.ts`)
- **Kind:** `INTERNAL`
- **Attrs:** `homespun.skills.count`

---

## Drift check

Each tier's test suite parses this file, collects H3 section names, and
compares against ActivitySource / tracer emissions in that tier's source.

- Tier tests reject undocumented span names and orphan dictionary entries.
- Dynamic (interpolated) span names — e.g. `SignalR.{hub}/{method}`,
  `homespun.user.action.<name>` — live in the allowlist inside each test
  with a comment explaining why static match is impossible.

## Workflow for changes

Add, rename, or remove a span → update this file in the same PR. CI fails
otherwise. See `docs/traces/README.md`.
