## Context

Today the Homespun session pipeline emits OTel spans that include only a truncated `homespun.content.preview` attribute (default 0 chars in Production, 80 in Development, 240 in Mock), enforced at fanout time by `OtlpScrubber`. Full A2A bodies are persisted to per-session JSONL via `A2AEventStore` for replay, but never reach Seq or the Aspire dashboard. The worker has a `DEBUG_AGENT_SDK=true` toggle that emits full SDK rx/tx bodies via `sdkDebug()`, but it covers only the SDK boundary inside the worker — the AG-UI translator on the server, the SignalR broadcast, the replay endpoint, and the browser-side reducer all log nothing about the events flowing through them.

The Aspire dashboard appears richer than Seq today because (a) it surfaces resource stdout/stderr alongside OTel signals, and (b) its trace UI expands span attributes inline, while Seq's list view shows the bare span/log name and requires a click to reveal structured properties. The data is on the wire in both sinks (`AddSeqEndpoint("seq")` registers OTLP exporters for logs *and* traces — see `Homespun.ServiceDefaults/Extensions.cs:64-93`), so the parity gap is presentation-shaped, not delivery-shaped.

Container-mode launch profiles (`dev-container`) all run as `ASPNETCORE_ENVIRONMENT=Mock` against the host's default `~/.homespun` data root with seeded demo projects. There is no profile for "boot the prod topology against the prod data location with no seeding" — useful for reproducing prod issues locally, sanity-checking deployments, and validating the empty-state UX.

Stakeholders: developers debugging session pipeline issues; the on-call workflow that uses Seq as the canonical observability sink in prod.

## Goals / Non-Goals

**Goals:**
- One env var (`HOMESPUN_DEBUG_FULL_MESSAGES=true`) opts the entire stack into full-body logging for A2A, AG-UI, and Claude Agent SDK messages.
- Full bodies appear as OTel **log events** (not span attributes) in both Seq and the Aspire dashboard, so they are not subject to the OTel attribute size limit.
- Existing and new log sites use rendered Serilog-style message templates so Seq's log list shows the value inline.
- A `prod` launch profile boots the container hosting topology against `~/.homespun-container/data` with `ASPNETCORE_ENVIRONMENT=Production` and no mock seeding, including Seq.
- The Aspire-vs-Seq display delta is verified empirically (spike, before code lands) so the message-template work is grounded in observed behaviour rather than the architectural hypothesis above.

**Non-Goals:**
- Changing default verbosity. The flag defaults to off in every profile (including `prod`); when off, behaviour is unchanged from today.
- Per-tier independent toggles (e.g., a worker-only knob that is silent on the server). The single umbrella toggle is the contract.
- Replacing the existing `homespun.content.preview` span attribute. Bodies on spans remain truncated; the new logs are additive.
- Rewriting `DEBUG_AGENT_SDK`. The new umbrella *implies* it; the underlying var continues to work standalone for back-compat with existing dev workflows.
- Hot-reload of `HOMESPUN_DEBUG_FULL_MESSAGES`. The flag is read at process start. Changes require a restart.
- A real Production deployment of the new `prod` profile. It is a local-development convenience that mirrors prod hosting; Komodo + `docker-compose.yml` remains the deployment path.

## Decisions

### Decision: Use OTel log events (not span attributes) for full bodies

**Choice:** Emit full A2A / AG-UI / SDK bodies via `ILogger` (server) and the worker's pino → OTel logger bridge, *additive* to the existing `homespun.content.preview` span attribute.

**Why:**
- OTel SDKs cap span attributes (default ~12 KB per attribute in many implementations); large tool inputs and file reads will silently truncate at the SDK layer before `OtlpScrubber` even sees them. Log events have no equivalent cap on the wire.
- Log entries display naturally as a flat list in Seq, matching the user's mental model of "show me every message that flowed".
- Span attributes still serve their role: a fast inline preview when scanning a trace, capped at a small size.

**Alternatives considered:**
- *Raise span attribute cap.* Rejected: requires per-SDK configuration, not universally honoured by collectors, and bloats traces even when no one is debugging.
- *Span events* (`activity.AddEvent(...)`). Rejected: span events display awkwardly in Seq's trace UI and are not surfaced in log queries.

### Decision: `ContentPreviewChars: -1` is the "no truncation" sentinel

**Choice:** Extend the existing `SessionEventContentOptions.ContentPreviewChars` int with a `-1` sentinel meaning "do not truncate". `OtlpScrubber` short-circuits its truncation pass when it observes `-1`. The `HOMESPUN_DEBUG_FULL_MESSAGES=true` umbrella sets this value via env var (`SessionEventContent__ContentPreviewChars=-1`).

**Why:**
- Single configuration point for the existing scrubber + the new log sites.
- No new public surface; existing consumers continue to read an int.
- `-1` is a familiar "unbounded" sentinel in similar APIs (e.g., timeouts).

**Alternatives considered:**
- *Separate `IncludeFullBody` boolean.* Rejected: doubles the surface area and creates a state where someone could set both inconsistently (e.g., `IncludeFullBody=true, ContentPreviewChars=10`).
- *`int.MaxValue` instead of `-1`.* Rejected: less obvious as a sentinel; risks accidental allocation of huge buffers in code that pre-allocates based on the value.

### Decision: One umbrella env var, with `DEBUG_AGENT_SDK` and `CONTENT_PREVIEW_CHARS` as derived defaults

**Choice:** `HOMESPUN_DEBUG_FULL_MESSAGES=true` is read by the AppHost (or each tier's startup) and:
- On the worker container env: sets `DEBUG_AGENT_SDK=true` and `CONTENT_PREVIEW_CHARS=-1` if not already set.
- On the server env: sets `SessionEventContent__ContentPreviewChars=-1` if not already set.
- On the web build env: sets `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true` (build-time, since Vite needs it baked in).

The umbrella does not *override* values that are explicitly set, so existing workflows (e.g., a developer who runs the worker container with `DEBUG_AGENT_SDK=true` only) still work.

**Why:** Single onboarding instruction ("set `HOMESPUN_DEBUG_FULL_MESSAGES=true`"), no surprise interactions with existing tier-specific knobs.

### Decision: Web client gating is build-time, not runtime

**Choice:** The browser-side debug logging is gated on `import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES === 'true'`. Tree-shakes out in production builds where the flag is unset.

**Why:** Avoids shipping debug-logging code to end users; avoids a server round-trip for a setting whose value never changes within a session. Cost: requires a Vite rebuild to toggle. Acceptable because the flag's intended use is "set it before you start debugging", not "toggle mid-session".

### Decision: Seq-friendly templates use placeholders for the values you want to see in the list

**Choice:** Adopt a convention for new and modified log sites: the message template includes `{Body}` (or whatever attribute carries the payload) so Seq's log list renders it inline. Existing sites that emit only an event name (e.g., a hypothetical `_logger.LogInformation("CMD.run", ...)`) are *not* refactored as part of this change — only sites added or touched here.

**Why:** Limits scope. A repo-wide template audit is a separate concern; this change focuses on the new session-pipeline log sites and validates the convention there.

### Decision: `prod` profile bind-mounts `~/.homespun-container/data` and runs server as root

**Choice:** New `prod` launch profile in `Properties/launchSettings.json` sets `HOMESPUN_DEV_HOSTING_MODE=container`, `ASPNETCORE_ENVIRONMENT=Production`, and a new `HOMESPUN_PROFILE_KIND=prod` discriminator. The AppHost branches on `HOMESPUN_PROFILE_KIND=prod` inside the `isContainerHosting` block to:
- Skip `HOMESPUN_MOCK_MODE`, `MockMode__*` env vars.
- Add `WithBindMount(prodDataPath, "/data")` and `WithEnvironment("HOMESPUN_DATA_PATH", "/data/.homespun/homespun-data.json")`.
- Continue running the server container as `--user 0:0` (matches dev-container; required for the docker.sock DooD path).
- Continue starting Seq.

`prodDataPath` resolves to `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".homespun-container", "data")`. The folder is created if missing (empty); a populated folder is reused as-is.

**Why:**
- Profile-discriminator env (`HOMESPUN_PROFILE_KIND`) keeps the AppHost branch readable and avoids brittle string comparisons against `ASPNETCORE_ENVIRONMENT` (which the existing branches already inspect).
- `~/.homespun-container/data` matches the documented prod path (`docs/installation.md`, `docs/troubleshooting.md`).
- Running as root is the same dev-only concession as dev-container; revisiting this is out of scope.

**Alternatives considered:**
- *Reuse `ASPNETCORE_ENVIRONMENT=Production` as the discriminator.* Rejected: the existing `Mock` env value drives a lot of MockServiceExtensions wiring; adding a third value keeps the conditional logic localized and reviewable.

### Decision: The verification spike is a tasks.md item, not a separate artifact

**Choice:** Include the "boot dev-mock, fire a CMD.run, screenshot Seq vs Aspire" spike as task #1 in tasks.md, blocking the message-template work. Output is a short note attached to the change folder (e.g., `notes/seq-display-spike.md`) — not a new spec artifact.

**Why:** Keeps the spike inside the proposal lifecycle (it gets archived with the change) without standing up a heavyweight design loop. If the spike invalidates the rendering hypothesis, we update design.md and re-plan; the current design.md will be the rollback point.

## Risks / Trade-offs

- **[Risk] Secrets in Seq.** Full bodies will include user prompts, file contents, tool outputs, and possibly credentials embedded in code. → Mitigation: the umbrella flag defaults to off everywhere (including the new `prod` profile). The existing `OtlpScrubber` secret-substring redaction continues to apply to attributes (it does not currently scan log bodies; we accept that gap for the debug path). Document the risk in the troubleshooting recipe.

- **[Risk] Replay double-logging.** `GET /api/sessions/{id}/events` re-translates A2A events through the same translator as the live path. With full-body logging on, replay calls will emit `agui.translate body=...` entries that duplicate the originals. → Mitigation: the replay-path log site tags entries with `homespun.replay=true` so Seq users can filter them out; the live-path tag is absent.

- **[Risk] Web-client OTLP volume.** Echoing every envelope from the browser through `/api/otlp/v1/logs` then back out to Seq doubles network traffic on the OTLP path. → Mitigation: build-time gating means the code is absent from non-debug builds; the `prod` profile defaults to off so the round-trip never happens unless explicitly enabled.

- **[Trade-off] Span size limit unaddressed.** We deliberately keep the existing 80–240-char `homespun.content.preview` truncation on spans even when `-1` is set on logs. → A user who clicks a span in Aspire still sees a small preview; full bodies live in the correlated log entries. Marginally less convenient than "everything inline on the span", but avoids SDK-level truncation surprises and keeps the trace view scannable.

- **[Risk] Spike invalidates the rendering hypothesis.** If the screenshots show that Seq genuinely loses structured attributes (not just collapses them visually), the message-template-only fix is insufficient. → Mitigation: the spike is task #1 and blocks subsequent work. If invalidated, we update design.md (likely toward "promote key span attributes to log events too") and re-plan tasks 2+ before writing code.

- **[Risk] `prod` profile masks deployment-pipeline bugs.** Local prod-mode succeeding does not prove the docker-compose deployment is correct (image tags, network names, env injection differ). → Mitigation: scope `prod` as a debugging tool, not a deployment gate. CLAUDE.md and the new troubleshooting note call this out explicitly.

- **[Risk] First-run on empty data folder.** Server initialization paths assume `~/.homespun` exists; an empty `~/.homespun-container/data` may hit unexpected null paths. → Mitigation: tasks.md includes a smoke task to boot `prod` against an empty folder and exercise the project-list, issue-list, and session-create flows. Any null-path regressions get fixed in the same change.

## Migration Plan

No data migration. The umbrella env var is additive; absence preserves today's behaviour. The `-1` sentinel is opt-in per-environment via configuration; existing `appsettings.*.json` values continue to work unchanged. The `prod` profile is a new launch entry; existing `dev-*` profiles are untouched.

Rollback: revert the change. No persisted state depends on it.

## Open Questions

- Should the `cmd.run` span (and similar pre-existing sites flagged by the spike) be retrofitted to the new template convention as part of this change, or deferred to a follow-up? Default deferral; will revisit after the spike.
- Should we add a `HOMESPUN_DEBUG_FULL_MESSAGES` env var to docker-compose.yml for the prod-deploy path? Default no — debug logging in real prod is out of scope; the flag exists for local debugging only.
