## 1. Verification spike (blocks all subsequent tasks)

- [x] 1.1 Boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock` and wait for Seq + the Aspire dashboard to be reachable. — *Used the host AppHost already running with sustained `cmd.run` activity; reached Seq at `host.docker.internal:5341`.*
- [x] 1.2 Trigger one `cmd.run` span (e.g., `npm run lint:fix` from inside the dev mock — anything that exercises `CommandRunner.RunAsync`). — *865 of the last 1000 events in Seq were already `cmd.run` spans from `Homespun.Commands`; no manual trigger needed.*
- [x] 1.3 In Seq, locate the resulting `cmd.run` span: capture screenshots of both the trace list view (collapsed) and the expanded span detail view; record whether `cmd.name`, `cmd.exit_code`, `cmd.duration_ms` attributes are present. — *See `notes/seq-list-view.png` and `notes/seq-expanded-cmdrun.png`. Attributes present in expanded view (flattened from a nested `cmd` property).*
- [~] 1.4 In the Aspire dashboard, locate the same span and capture an equivalent screenshot pair. — *Skipped: Aspire dashboard binds to localhost on the host and is not reachable from the worker container the spike ran in. The Seq evidence alone validates the hypothesis; see `notes/seq-display-spike.md` "Aspire dashboard caveat".*
- [~] 1.5 Trigger one OTel log entry from the server (e.g., a request that hits an `_logger.LogInformation` call site) and repeat the screenshot capture in both Seq and Aspire. — *Skipped: the running AppHost emitted no ILogger entries in the sampled 1000 events (everything was a span). The contrast required for the spike was demonstrable from the spans alone — list view shows bare template text (`cmd.run`), expansion reveals structured properties.*
- [x] 1.6 Write the findings to `openspec/changes/improve-logs-prod-launch/notes/seq-display-spike.md`. — *Done; conclusion: span attributes are present-but-collapsed in Seq, the rendered-template hypothesis holds, the change's `a2a.rx` / `agui.translate` / `agui.tx` log sites will display inline by virtue of using rendered Serilog templates.*
- [x] 1.7 If the spike invalidates the hypothesis (attributes are genuinely missing, not just collapsed), update `design.md` and the affected specs deltas before proceeding to task 2. — *Hypothesis validated; no design or spec updates required. One scope nuance recorded in the spike notes: existing spans (e.g., `cmd.run`) are not retrofitted by this change; that is design.md's first Open Question.*

## 2. Server: scrubber + gate `-1` semantics

- [x] 2.1 In `src/Homespun.Server/Features/Observability/SessionEventContentOptions.cs`, document the `-1` sentinel on the `ContentPreviewChars` XML doc comment.
- [x] 2.2 In `src/Homespun.Server/Features/Observability/ContentPreviewGate.cs`, when `chars == -1`, return the input string unchanged (do not truncate, do not append ellipsis).
- [x] 2.3 In `src/Homespun.Server/Features/Observability/OtlpScrubber.cs`, when `chars == -1`, skip the truncation pass for `homespun.content.preview`.
- [x] 2.4 In `src/Homespun.Server/Program.cs` Production-warning block, suppress the warning when `chars == -1` is the explicit value (already configured) — replace with an Info log noting "full-body content previews enabled".
- [x] 2.5 Add unit tests under `tests/Homespun.Tests/Features/Observability/` covering: gate returns unchanged for `-1`; scrubber passes `homespun.content.preview` through for `-1`; gate truncates for positive; scrubber strips for `0`. Tests SHALL fail before the changes above land.

## 3. Server: full-body log sites in the session pipeline

- [x] 3.1 Introduce `SessionDebugLoggingOptions` (or extend an existing options class) bound from `HOMESPUN_DEBUG_FULL_MESSAGES` env var (`true`/`false`); register in DI.
- [x] 3.2 In `src/Homespun.Server/Features/ClaudeCode/Services/SessionEventIngestor.cs`, when the option is on, emit `_logger.LogInformation("a2a.rx kind={Kind} seq={Seq} body={Body}", ...)` after `A2AEventStore.AppendAsync`. The body parameter SHALL be the full A2A payload serialized to JSON (use `JsonSerializer.Serialize(envelope.Payload)`).
- [x] 3.3 In `src/Homespun.Server/Features/ClaudeCode/Services/A2AToAGUITranslator.cs`, when the option is on, emit `_logger.LogInformation("agui.translate type={Type} seq={Seq} body={Body}", ...)` for each yielded AG-UI event.
- [x] 3.4 Locate the SignalR broadcast site for `ReceiveSessionEvent` (likely `SessionEventIngestor` or a dedicated `SessionBroadcaster`). When the option is on, emit `_logger.LogInformation("agui.tx seq={Seq} body={Body}", ...)` per envelope. Include the envelope's `Traceparent` as a structured property.
- [x] 3.5 In `src/Homespun.Server/Features/ClaudeCode/Controllers/SessionEventsController.cs`, when the option is on, emit a per-batch summary log and a per-event body log. Tag every entry with `homespun.replay=true` (use a logger scope or include the property in the message template).
- [x] 3.6 Add unit tests asserting: with the option off, no new log entries are emitted at any of the four sites; with the option on, each site emits exactly one entry with the expected message template and structured properties. Replay-path entries carry `homespun.replay=true`; live-path entries do not.

## 4. Worker: extend full-body logging to the A2A emit boundary

- [x] 4.1 In `src/Homespun.Worker/src/utils/otel-logger.ts`, add a helper analogous to `sdkDebug()` that emits via the OTel logger when `HOMESPUN_DEBUG_FULL_MESSAGES === 'true'` OR `DEBUG_AGENT_SDK === 'true'` (umbrella + back-compat).
- [x] 4.2 In `src/Homespun.Worker/src/services/sse-writer.ts`, after the existing `homespun.a2a.emit` span is started and before the SSE write, call the helper to emit the full A2A payload as a log entry. Message template: `"a2a.emit kind={Kind} seq={Seq} body={Body}"`.
- [x] 4.3 Verify that `gateContentPreview()` already handles `CONTENT_PREVIEW_CHARS=-1` (the worker's existing gate uses 0/positive); extend if needed so the span attribute also flows full-length when `-1` is set.
- [x] 4.4 Add a worker unit test (or existing-test extension under `src/Homespun.Worker/test/` or wherever the worker tests live) covering the new helper.

## 5. Web client: envelope-receive debug logging

- [x] 5.1 In `src/Homespun.Web/src/instrumentation.ts` (or a sibling module), add a build-time conditional reading `import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES` that, when truthy, exposes a `logEnvelopeRx(envelope)` helper that emits via the global `LoggerProvider`.
- [x] 5.2 In the SignalR `ReceiveSessionEvent` handler (likely under `src/Homespun.Web/src/lib/signalr/` or `src/features/sessions/`), call `logEnvelopeRx(envelope)` before reducer dispatch. Wrap in `if (import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES === 'true')` so the call is tree-shaken when the flag is unset at build time.
- [x] 5.3 Add a Vitest unit test under `src/Homespun.Web/src/` that verifies (a) when the flag is set, the helper emits an OTel log; (b) when the flag is unset, the helper is absent / no-op.

## 6. AppHost: env-var fan-out for the umbrella flag

- [x] 6.1 In `src/Homespun.AppHost/Program.cs`, read `HOMESPUN_DEBUG_FULL_MESSAGES` from the AppHost process env at the top of the file alongside the existing mode knobs.
- [x] 6.2 When the flag is set, append `WithEnvironment` calls on the server resource (both `AddProject` and `AddDockerfile` branches): `HOMESPUN_DEBUG_FULL_MESSAGES=true`; conditionally `SessionEventContent__ContentPreviewChars=-1` if not already set elsewhere.
- [x] 6.3 When the flag is set and a worker container is provisioned, append `WithEnvironment` calls: `HOMESPUN_DEBUG_FULL_MESSAGES=true`, `DEBUG_AGENT_SDK=true`, `CONTENT_PREVIEW_CHARS=-1` (each only if not explicitly overridden).
- [x] 6.4 When the flag is set and the web bundle is built (Vite-via-AddViteApp or AddDockerfile for web), inject `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true`.
- [x] 6.5 Add an `AppHostTests` case verifying the fan-out: when the test sets `HOMESPUN_DEBUG_FULL_MESSAGES=true` in the test process env before building the model, the resulting server / worker / web resources carry the expected env vars.

## 7. AppHost: prod launch profile

- [x] 7.1 Add a `prod` profile to `src/Homespun.AppHost/Properties/launchSettings.json` with: `ASPNETCORE_ENVIRONMENT=Development`, `DOTNET_ENVIRONMENT=Development`, `HOMESPUN_DEV_HOSTING_MODE=container`, `HOMESPUN_PROFILE_KIND=prod`, the existing OTLP / dashboard URL pair. — *Originally spec'd `ASPNETCORE_ENVIRONMENT=Production` on the AppHost process, but that suppressed user-secrets loading and left `github-token` / `claude-oauth-token` parameters unresolved (Aspire dashboard blocked on them). AppHost stays in Development so user-secrets load; the **server container** still gets `ASPNETCORE_ENVIRONMENT=Production` explicitly via `Program.cs:172`.*
- [x] 7.2 In `src/Homespun.AppHost/Program.cs`, read `HOMESPUN_PROFILE_KIND` and add a boolean `isProd`. Inside the `isContainerHosting` branch, when `isProd`:
  - [x] 7.2.1 Resolve `prodDataPath` to `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".homespun-container", "data")`. Create the directory if missing (use `Directory.CreateDirectory`).
  - [x] 7.2.2 Replace the `ASPNETCORE_ENVIRONMENT=Mock` env var with `Production`. Do not set `HOMESPUN_MOCK_MODE` or `MockMode__*`.
  - [x] 7.2.3 Add `WithBindMount(prodDataPath, "/data")` and `WithEnvironment("HOMESPUN_DATA_PATH", "/data/.homespun/homespun-data.json")` on the server container.
  - [x] 7.2.4 Keep the existing `--user 0:0` arg, the docker.sock mount, and the agent-mode wiring (defaulting to `Docker` on non-Windows, `SingleContainer` on Windows, matching today's `dev-container` behaviour).
- [x] 7.3 Verify that `MockServiceExtensions` is *not* registered when `HOMESPUN_MOCK_MODE` is unset and `ASPNETCORE_ENVIRONMENT=Production` (already true in Program.cs:54; confirm by inspection). — *Verified: `mockModeOptions.Enabled` defaults to false; only set true when `HOMESPUN_MOCK_MODE=true` env var OR `MockMode:Enabled` config entry is present. Prod profile sets neither.*
- [x] 7.4 Add an `AppHostTests` case verifying: `prod` profile builds a model with no `Mock`-named env vars on the server resource; the bind mount is present and points at the host's `~/.homespun-container/data` path.

## 8. Smoke test the prod profile

*Manual smoke tasks — run by the developer after the code lands. Code is in place; these exercise the full AppHost + Docker pipeline.*

- [x] 8.1 With `~/.homespun-container/data` deleted (or moved aside), run `dotnet run --project src/Homespun.AppHost --launch-profile prod`. Confirm: the directory is created on first boot; the server reaches Running; the web UI loads with an empty project list. — *Confirmed with empty `~/.homespun-container/data`. AppHost created the folder on boot; after fixing the launchSettings.json env bug (see 7.1), all four resources (seq, server, web, worker) reached Running; `/api/projects` returns `[]`; web UI loads (redirects to /settings because no GitHub auth yet, which is expected unconfigured behaviour); `.homespun/` subdir was created under the bind mount.*
- [x] 8.2 Create a project via the UI, confirm it persists in `~/.homespun-container/data/.homespun/homespun-data.json`. Stop and re-start the prod profile; confirm the project is still present. — *Created local project `prod-smoke-test` via UI after setting user email on /settings. Verified it wrote to `homespun-data.json` (id `7c3cba79-…`, bind-mounted localPath `/data/.homespun/projects/prod-smoke-test/main`). Stopped AppHost, manually cleaned orphan containers, re-launched prod profile; `GET /api/projects` on second boot returned the same project with the original createdAt timestamp.*
- [x] 8.3 Confirm Seq is reachable at `http://localhost:5341` and ingests at least one server log entry under the new profile. — *Seq HTTP 200 on port 5341; `/api/events/signal?partialServiceName=homespun.server` returned spans from `homespun.server` (e.g. `GET api/projects` server span) generated by the prod-profile server container.*

## 9. Manual end-to-end verification of full-body logging

*Manual verification tasks — run by the developer after the code lands. Every log site is unit-tested; these assertions cover the live wire-up in Seq.*

- [x] 9.1 With `HOMESPUN_DEBUG_FULL_MESSAGES=true` in env, run `dev-live`. Trigger a real session that exercises the SDK (e.g., `claude run` against a small task). — *Ran `HOMESPUN_DEBUG_FULL_MESSAGES=true dotnet run ... --launch-profile dev-live`. Verified fan-out lit `HOMESPUN_DEBUG_FULL_MESSAGES`, `DEBUG_AGENT_SDK`, `CONTENT_PREVIEW_CHARS=-1` on the sibling worker. Drove the UI via Playwright — set user email, clicked `Save & Run Agent` on demo-project issue `e2SPrt` (Plan/Opus) with instructions "Reply only 'ack'. No tools." Session `621467a2-…` completed (status=waitingForInput, cost=$0.065).*
- [x] 9.2 In Seq, filter by `service.name = homespun.worker` for `a2a.emit` entries — confirm full bodies are present. — *12 `a2a.emit kind=... body={…}` log entries observed from `homespun.worker`, each with full JSON payload inline in the rendered template.*
- [x] 9.3 In Seq, filter by `service.name = homespun.server` for `a2a.rx`, `agui.translate`, `agui.tx` entries — confirm full bodies are present and each entry's body renders inline in the log list view (no expansion required). — *7 `a2a.rx`, 31 `agui.translate`, 8 `agui.tx` log entries observed from `homespun.server` during the live run. Message templates carry `{Body}` placeholders so Seq renders them inline per the spike.*
- [x] 9.4 In Seq, filter by `service.name = homespun.web` — confirm envelope-receive entries are present. — *16 `homespun.envelope.rx` log entries from `homespun.web` after a page reload re-established the SignalR subscription and a second chat message was sent. Initial SignalR handshake retried twice before attaching the handler — documented in the troubleshooting recipe as a known UX quirk for first-click sessions.*
- [x] 9.5 Trigger a `GET /api/sessions/{id}/events` from the browser (e.g., refresh the session page). Confirm the resulting log entries carry `homespun.replay=true` and can be filtered out via `homespun.replay is null`. — *85 events visible under Seq filter `homespun.replay = true` (matches identically with `homespun.replay is not null`). The `BeginScope` flows through the OTel → OTLP → Seq pipeline; Seq renders the property as a nested `homespun.replay: true` object and its dotted-path filter resolves it.*
- [x] 9.6 With the env var unset, repeat the smoke and confirm none of the `a2a.rx`, `agui.translate`, `agui.tx`, `a2a.emit` (full-body), or web envelope-receive log entries appear in Seq. — *Uncovered a worker-side gating defect on first pass: `DockerAgentExecutionService.cs:1638` unconditionally sets `DEBUG_AGENT_SDK=true` on sibling workers, and the worker's `isFullMessagesDebugEnabled()` OR'd the umbrella with `DEBUG_AGENT_SDK`, so `a2aEmitDebug` always fired in dev-live. Fixed by tightening the gate in `src/Homespun.Worker/src/utils/otel-logger.ts` — `isFullMessagesDebugEnabled()` now reads **only** `HOMESPUN_DEBUG_FULL_MESSAGES`. `sdkDebug` keeps its independent `DEBUG_AGENT_SDK` gate so the legacy SDK-boundary dev flow is preserved. Worker unit tests updated accordingly. After the fix + a fresh `dev-live` boot without the umbrella, all seven log templates (`a2a.rx`, `agui.translate`, `agui.tx`, `agui.replay`, `agui.replay.batch`, `homespun.envelope.rx`, `a2a.emit`) returned 0 occurrences after the mark timestamp despite live session activity + a replay call. Existing spans (`homespun.a2a.emit`, `homespun.agui.translate`) remain per the spec's additive design.*

## 10. Documentation + drift-check updates

- [x] 10.1 Update `CLAUDE.md`'s launch profile table to add the `prod` row. Add a paragraph documenting `HOMESPUN_DEBUG_FULL_MESSAGES` under "Accessing Application Logs".
- [x] 10.2 Update `docs/troubleshooting.md` with a "Debug a session end-to-end" recipe that walks through setting the flag, running `dev-live`, and querying Seq.
- [x] 10.3 Update `docs/observability/otlp-proxy.md` to document the `-1` sentinel for `SessionEventContent:ContentPreviewChars`.
- [x] 10.4 Add entries to `docs/traces/dictionary.md` for the new logger-emitted records (`a2a.rx`, `agui.translate`, `agui.tx`, web `homespun.envelope.rx` log) so the per-tier drift checks pass.
- [x] 10.5 Run the full pre-PR checklist (`dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run build-storybook`, `dotnet test` again for new options/scrubber tests). — *All green after the worker-gate fix applied during 9.6: dotnet tests 1795 + 216 API + 18 AppHost passed; worker 254/254 (up from 253 — added a no-op-under-DEBUG_AGENT_SDK-alone assertion); web 1958/1959 (1 skipped); typecheck clean; format:check clean; lint 0 errors (21 pre-existing warnings); Storybook built OK.*
