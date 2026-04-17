## Why

The A2A-native session event pipeline (landed in commit `e6ae69b9`) crosses six hops between the Claude Agent SDK and the rendered UI — worker SDK receive → worker A2A emit → server SSE receive → server ingest+append → server translate → SignalR broadcast → client receive → client reducer apply. Users are observing assistant messages that the worker logs as `[SDK rx]` but never render in the client, and we have no way to pinpoint which hop drops them. Separately, `DockerAgentExecutionService` uses Docker-outside-of-Docker, which does not work on Windows from a host `dotnet run` — so the author cannot reproduce or debug the symptom locally. Both gaps need to close before we can fix the underlying message-loss bug (or the thinking-bubble UX, or the dedup-key issue) with evidence.

## What Changes

- Add a `SessionEventLog` logger category with a shared JSON field schema across worker (TypeScript), server (Serilog), and client (new API endpoint). Correlation by A2A `contextId`/`taskId` and per-event `messageId`/`artifactId` as recommended by the A2A spec's observability guidance.
- Emit `Information`-level structured entries at six pipeline hops: `worker.a2a.emit`, `server.sse.rx`, `server.ingest.append`, `server.agui.translate`, `server.signalr.tx`, `client.signalr.rx`, `client.reducer.apply`. Every entry carries the correlation fields plus an optional content preview.
- Make content-preview length configurable via `SessionEventLog:ContentPreviewChars` (default 80 in Development, 0 = disabled in Production). Same configurability pattern as the worker's existing `DEBUG_AGENT_SDK`.
- Add **`POST /api/log/client`** endpoint that accepts batched client log entries and forwards them to Serilog under `SourceContext="Homespun.ClientSessionEvents"`. Client-side batcher flushes on 50 entries or 500 ms, whichever first, and is self-defensive (never logs its own fetch errors through itself).
- Add `SingleContainerAgentExecutionService`, a dev-only `IAgentExecutionService` shim that points every session at a pre-running `homespun-worker` docker-compose container via configuration (`AgentExecution:SingleContainer:WorkerUrl`). Registration is gated on `IsDevelopment()` AND `AgentExecution__Mode=SingleContainer` so production misconfiguration fails fast at startup.
- Enforce single-active-session in the shim: a second concurrent `StartSessionAsync` throws `SingleContainerBusyException`, surfaced to the UI as a SignalR toast via `INotificationService` and logged at `Error` with both sessionIds.
- Extend `scripts/mock.sh` and `scripts/mock.ps1` with a `--with-worker` flag that brings up the compose `worker` service, waits for its healthcheck, exports its URL, and sets the agent mode to `SingleContainer`. The scripts warn if `CLAUDE_CODE_OAUTH_TOKEN` is unset before starting the worker.
- Expose the `worker` container's port on the host in `docker-compose.yml` so the Windows `mock.sh` shim can reach it without rewriting the compose network.
- Add a one-line comment at `agui-reducer.ts` `applyRunFinished` explaining the deliberate non-rendering of `RunFinished.result` (the result is a completion signal, not visible content — see design.md for rationale).

**Non-goals, explicit:** no dedup-key fix in `use-session-events.ts`; no thinking-bubble UI redesign; no change to A2A → AG-UI translator semantics; no rendering of `RunFinished.result`. These are tracked as separate follow-up changes to be scoped with evidence from the telemetry this change introduces.

## Capabilities

### New Capabilities
- None. SingleContainer execution is a dev-only shim for the existing `claude-agent-sessions` capability, not a new product capability.

### Modified Capabilities
- `session-messaging`: adds requirements for observable, correlated logging across the live-delivery pipeline (worker → server → client). The capability's existing live-equals-refresh correctness requirements are unchanged; this change extends it with diagnostics so future regressions can be located hop-by-hop.

## Impact

### New files
- `src/Homespun.Server/Features/ClaudeCode/Services/SingleContainerAgentExecutionService.cs`
- `src/Homespun.Server/Features/ClaudeCode/Exceptions/SingleContainerBusyException.cs`
- `src/Homespun.Server/Features/Observability/SessionEventLog.cs` — shared category constants and helpers
- `src/Homespun.Server/Features/Observability/ClientLogController.cs` — `POST /api/log/client`
- `src/Homespun.Server/Features/Observability/ClientLogEntry.cs` — DTO
- `src/Homespun.Web/src/lib/session-event-log.ts` — client batcher and helpers

### Modified files
- `src/Homespun.Worker/src/utils/logger.ts` — add structured `sessionEventLog()` helper preserving readable browser/console output
- `src/Homespun.Worker/src/services/sse-writer.ts` — emit `worker.a2a.emit`
- `src/Homespun.Server/Features/ClaudeCode/Services/DockerAgentExecutionService.cs` — emit `server.sse.rx`
- `src/Homespun.Server/Features/ClaudeCode/Services/A2AEventStore.cs` — emit `server.ingest.append`
- `src/Homespun.Server/Features/ClaudeCode/Services/SessionEventIngestor.cs` — emit `server.agui.translate` and `server.signalr.tx`
- `src/Homespun.Web/src/features/sessions/hooks/use-session-events.ts` — emit `client.signalr.rx` and `client.reducer.apply`
- `src/Homespun.Web/src/features/sessions/utils/agui-reducer.ts` — document deliberate non-render of `RunFinished.result`
- `src/Homespun.Server/Program.cs` — register `SessionEventLog` options, client log controller, and `SingleContainerAgentExecutionService` conditionally
- `src/Homespun.Server/appsettings.json` / `appsettings.Development.json` — defaults for `SessionEventLog:ContentPreviewChars`
- `scripts/mock.sh`, `scripts/mock.ps1` — `--with-worker` flag
- `docker-compose.yml` — expose worker port on host for Windows dev

### Dependencies
- None new. Uses existing Serilog configuration on the server and existing Pino-shaped JSON logging on the worker.

### Deployment
- No data migrations. New configuration keys default to safe (no-op) values in Production. Existing `AgentExecution__Mode=Docker` deployments are unaffected; `SingleContainer` mode is developer-only.

### Testing
- Unit: `SessionEventLog` field schema, content-preview truncation behaviour, log-category naming.
- Unit: `SingleContainerAgentExecutionService` busy guard, URL-only pass-through, dev-only registration guard.
- Integration: `/api/log/client` happy path, malformed payload rejection, oversized payload rejection, rate-limit defence-in-depth.
- Integration regression: `RefreshFidelityTests` and `SessionEventIngestorTests` must remain green — logging must not reorder, drop, or duplicate envelopes.
- Manual: run `./scripts/mock.sh --with-worker` on Windows, send one turn, verify a Grafana LogQL query pinned to a single `messageId` returns the full six-hop chain.

### Risks
- **Log volume in Production**: mitigated by defaulting `ContentPreviewChars=0` in Production and allowing individual hops to be disabled per config. Correlation IDs alone carry negligible volume.
- **Client log feedback loop**: client batcher must never route its own fetch failures through `sessionEventLog`. It logs only via `console.warn` on its own errors, with no retry storm.
- **Single-container shared state**: two developers pointing at one shared compose worker would interleave sessions. Mitigated by the dev-only gate, the busy-exception surfacing, and a note in `docs/` about the single-user assumption.
- **Windows path mapping**: the compose `worker` has a Linux filesystem view; live sessions operate on container paths, not Windows host paths. Acceptable for pipeline-debugging purposes; documented in the README section accompanying the `--with-worker` flag.
- **`IsDevelopment()` gate bypass**: a Production environment that sets `ASPNETCORE_ENVIRONMENT=Development` alongside `AgentExecution__Mode=SingleContainer` would enable the shim. Mitigated by throwing at startup if the mode is `SingleContainer` but the environment is Production.
