## Why

Debugging an agent session today requires correlating fragments across three sinks: span tags carry only an 80–240-character `homespun.content.preview` slice, full A2A bodies live as raw JSONL on disk, and `DEBUG_AGENT_SDK=true` only covers the worker's SDK boundary. Spinning up a focused investigation means tailing files manually and inferring the rest. At the same time, Seq's display of structured log/span attributes is less inline-friendly than the Aspire dashboard's, and there is no launch profile that exercises the prod hosting topology against unseeded data — every container-mode profile today is `Mock`.

## What Changes

- **Full-body message logging via single env var.** A new `HOMESPUN_DEBUG_FULL_MESSAGES=true` toggle opts the entire stack (worker + server + web) into emitting full A2A, AG-UI, and Claude Agent SDK message bodies as OTel log events. The toggle implies `DEBUG_AGENT_SDK=true` on the worker and `SessionEventContent:ContentPreviewChars=-1` on the server (where `-1` is a new "no truncation" sentinel honoured by `OtlpScrubber`).
- **New log sites that today have none:**
  - `SessionEventIngestor` emits an `a2a.rx` log carrying the full A2A payload.
  - `A2AToAGUITranslator` emits an `agui.translate` log per emitted AG-UI event.
  - The SignalR broadcast site emits an `agui.tx` log per envelope.
  - `SessionEventsController` (replay) emits a per-batch summary plus optional per-event bodies.
  - The web client logs incoming envelopes via the existing OTLP proxy, gated on a build-time `VITE_HOMESPUN_DEBUG_FULL_MESSAGES`.
- **Seq-friendly message templates.** New and existing log sites use rendered templates (`"a2a.rx kind={Kind} body={Body}"`) rather than bare event names with detached structured properties, so Seq's log list view shows the values inline without requiring per-row expansion.
- **New `prod` launch profile.** `dotnet run --project src/Homespun.AppHost --launch-profile prod` runs the container hosting topology (server + web + worker built from local Dockerfiles, plus Seq) against a `~/.homespun-container/data` bind mount, with `ASPNETCORE_ENVIRONMENT=Production` and no mock seeding. First-run on an empty data directory is supported (folders are created on demand); a populated directory is reused as-is.
- **Verification spike** (executed as part of the implementation, before code changes land): boot `dev-mock`, fire a known span (e.g., `cmd.run`) and a known log, capture screenshots of the Seq trace + log views, and document whether structured attributes are present-but-collapsed vs. genuinely missing. The spike output anchors the message-template work in the rest of the change.

## Capabilities

### New Capabilities
<!-- None — this change extends existing capabilities. -->

### Modified Capabilities
- `observability`: Adds a "full-body session message logging" requirement and extends the existing scrubber requirement with the `-1` sentinel. Adds a Seq-friendly-rendering requirement covering message-template conventions.
- `dev-orchestration`: Adds a `prod` launch profile requirement alongside the existing `dev-*` profiles. Adds a documented `HOMESPUN_DEBUG_FULL_MESSAGES` env var that any profile may opt into.

## Impact

- **Backend**: `Homespun.Server/Features/ClaudeCode/Services/SessionEventIngestor.cs`, `A2AToAGUITranslator.cs`, `Controllers/SessionEventsController.cs`; `Homespun.Server/Features/Observability/OtlpScrubber.cs`, `SessionEventContentOptions.cs`, `ContentPreviewGate.cs`; `Homespun.Server/Program.cs` (Production-warning logic for `-1`).
- **Worker**: `Homespun.Worker/src/utils/otel-logger.ts`, `services/sse-writer.ts`, `routes/sessions.ts` (extend `sdkDebug` + add A2A emit-side body log; honour the new umbrella env var).
- **Web**: `Homespun.Web/src/instrumentation.ts`, `lib/signalr/trace.ts` (or sibling), `features/sessions/agui-reducer.ts` ingestion edge — emit a debug log per envelope when the build flag is set.
- **AppHost**: `Homespun.AppHost/Program.cs` (new `isProd` branch in container hosting block; data-volume bind mount; env-var fan-out for `HOMESPUN_DEBUG_FULL_MESSAGES`); `Homespun.AppHost/Properties/launchSettings.json` (new `prod` profile).
- **Docs**: `CLAUDE.md` (launch profile table + new debug env var), `docs/troubleshooting.md` (full-body debug recipe), `docs/observability/otlp-proxy.md` (scrubber `-1` semantics).
- **Tests**: `OtlpScrubber` unit tests for `-1`; `SessionEventIngestor` log-emission unit test; an AppHost smoke that confirms the `prod` profile boots and serves `/health` with no mock data.
- **Span dictionary**: `docs/traces/dictionary.md` — new `a2a.rx`, `agui.translate`, `agui.tx` logger names (drift check requires this).
