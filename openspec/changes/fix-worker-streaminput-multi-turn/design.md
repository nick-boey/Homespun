## Context

PR #776 (`simplify-worker-session-manager`) refactored the worker to back each session with a single long-lived `query()` call, delivering follow-up messages via `Query.streamInput(...)`. This design shipped with a spike (`scripts/spike-idle-tolerance.ts`) that claimed to confirm the approach survives long idle periods in streaming-input mode.

Empirical re-running of that spike after the merge — in both its original default/haiku configuration and in the failing Build-mode configuration — **consistently fails** with `ProcessTransport is not ready for writing` on the second turn. Reading the SDK source (`@anthropic-ai/claude-agent-sdk@0.2.81`, `sdk.mjs`) revealed the cause:

```
// Query.streamInput (paraphrased from minified sdk.mjs)
for await (const msg of stream) { transport.write(msg) }
if (count > 0 && hasBidirectionalNeeds()) await waitForFirstResult()
transport.endInput()   // closes stdin to CLI subprocess
```

The initial `AsyncIterable` prompt is internally consumed via the same `streamInput` routine, so even the initial `onceIterator(initialPrompt)` triggers `endInput()` once it returns. The CLI exits after turn 1; any subsequent `streamInput`, `setPermissionMode`, or `setModel` call on the `Query` throws `ProcessTransport is not ready for writing`.

A persistent-queue variant of the spike — where the initial `AsyncIterable` never returns until session close, and follow-ups are pushed into that same iterable — reliably survives multi-turn with the Build-mode configuration and `setPermissionMode` at the boundary. That is the shape this change adopts.

Separately: there is no debug channel that captures exactly what the worker sends to the SDK and what comes back. Diagnosing the above required reading the SDK's minified source. A runtime-toggleable capture point is needed so future agent-SDK regressions can be reproduced without source diving.

## Goals / Non-Goals

**Goals:**

- The `follow-up-prompts.live.test.ts` live test passes deterministically after this change.
- Multi-turn conversations in production (both `Build` and `Plan` modes) send and receive messages on a single long-lived CLI without restart.
- `setMode` and `setModel` mid-session apply to the live query without failing with `ProcessTransport is not ready for writing`.
- When `DEBUG_AGENT_SDK=true` is set, all four SDK-boundary points emit structured JSON logs on stdout in a format readable by both `docker logs` and the Promtail→Loki pipeline.
- `DEBUG_AGENT_SDK=false` (or unset) is the default in both compose and live-test invocations — zero overhead when not debugging.
- The updated `spike-idle-tolerance.ts` serves as a standalone regression probe for future SDK changes that might re-break this path.

**Non-Goals:**

- Rewriting the HTTP API surface or the A2A event translation.
- Adding persistence to the input queue (it is in-memory per-session; lost on worker restart, as the existing session state already is).
- Sending debug data to a destination other than stdout (no separate file writer, no external log collector; PLG already covers stdout).
- Pinning the SDK version — out of scope; noted as a follow-up in `proposal.md`.
- Reverting #776 wholesale — we keep its single-long-lived-query property; only the stdin-feed mechanism changes.

## Decisions

### 1. Persistent `InputQueue` instead of `onceIterator` + `q.streamInput()`

The worker defines an `InputQueue` class that implements `AsyncIterable<SDKUserMessage>`. Its iterator awaits indefinitely between pushes and only returns when `close()` is called. The `query({ prompt, options })` call receives this queue as `prompt`, and `inputQueue.push(initialPrompt)` is the first message. Follow-up messages are delivered via `inputQueue.push(followupMessage)` — **never** via `q.streamInput(...)`.

**Alternatives considered:**

- *Close-and-resume per turn* (the pre-#776 design): correct, but carries the per-turn CLI-restart cost #776 set out to remove, and loses `canUseTool` / pending-interaction state mid-stream. Rejected.
- *Call `q.streamInput()` but add a keep-alive message after each turn*: hacky; depends on SDK-internal timing and would still lose the CLI on the trailing `endInput()`. Rejected.
- *Fork or patch the SDK to remove `endInput()`*: invasive and fragile against SDK upgrades. Rejected.

**Rationale:** Mirrors the pattern validated by the updated spike in the failing configuration. Keeps the existing `OutputChannel` and `runQueryForwarder` design intact.

### 2. `runQueryForwarder` stays lifetime-scoped; per-HTTP-request SSE streams decouple via `OutputChannel`

`runQueryForwarder` continues to run one `for await (const msg of q)` for the session's lifetime, pushing every event into `session.outputChannel`. Per-HTTP-request `streamSessionEvents` consumes `outputChannel` per turn and returns on `result`. The consumer-side iterator termination must not propagate iterator-done into the SDK's `Query.sdkMessages` (the inner `g4` is single-shot: once `.return()` is called, it stays done forever).

`OutputChannel` already isolates the two. This change hardens it (see decision 3).

**Alternatives considered:**

- *Emit the SSE stream directly from the SDK query without an output channel*: would couple per-HTTP-request lifetime to per-session lifetime and re-introduce the iterator-done bug we just diagnosed. Rejected.
- *Keep the SSE stream open across turns and use chunked-transfer to signal turn boundaries*: breaks the existing server→worker contract and breaks the test fixture. Rejected as out of scope.

### 3. Harden `OutputChannel` against stale resolvers

`OutputChannel.[Symbol.asyncIterator]` keeps a `resolver` field pointing to the pending `Promise` resolver whenever a consumer is waiting. If the consumer's `for await` is aborted (e.g. `streamSessionEvents` returning on `result`), the `resolver` remains assigned to a discarded `Promise`. A concurrent `push(...)` then invokes that stale resolver — a no-op — and the event is silently lost.

Fix: wrap the generator body in a `try { ... } finally { this.resolver = null; }` so the field is cleared on any exit, including abort. Events pushed while no consumer is active land in `this.queue` and are delivered to the next consumer.

**Alternatives considered:**

- *Make `OutputChannel` multicast*: overkill; each event has exactly one logical consumer. Rejected.
- *Require the forwarder to wait for a consumer before pushing*: couples the SDK's drain rate to HTTP-request timing and risks back-pressuring the SDK. Rejected.

### 4. `DEBUG_AGENT_SDK` env var gates a single stdout channel

`logger.ts` grows one helper: `sdkDebug(direction: 'tx' | 'rx', msg: unknown)`. It is a no-op unless `process.env.DEBUG_AGENT_SDK === 'true'`. When enabled, it emits a single structured-JSON line via the existing `console.log`-backed logger so the line is indistinguishable in format from other worker logs (same `SourceContext`/`Component` shape, consumed by the same Promtail config).

The worker calls `sdkDebug('tx', sessionOptions)` immediately before `query({...})`; `sdkDebug('tx', userMessage)` inside `InputQueue.push(...)`; `sdkDebug('tx', { op: 'setPermissionMode', mode })` (and equivalent for `setModel`); and `sdkDebug('rx', msg)` at the top of `runQueryForwarder`'s `for await` body.

Env propagation: `docker-compose.yml` passes `DEBUG_AGENT_SDK=${DEBUG_AGENT_SDK:-false}` into the `worker:` service; `container-lifecycle.ts` reads `process.env.DEBUG_AGENT_SDK` and forwards it via `-e`. Both paths default to disabled; the host developer flips it on by exporting the variable.

**Alternatives considered:**

- *Log to a separate file under `/home/homespun/.claude/debug/`*: duplicates the existing `claude_sdk_debug.log` file watcher; requires the live-test fixture to read a mounted volume to capture. Rejected.
- *Reuse the existing `DEBUG_LOGGING=true` flag*: mixes two audiences (general debug output vs SDK-boundary capture). The SDK-boundary logs can be large, so keeping them gated separately lets developers dial them up without drowning in everything else. Rejected.
- *Emit at a named log level (`TRACE`)*: the current logger only has `Debug/Information/Warning/Error`. Adding a level is more invasive than a feature flag. Rejected.

### 5. Spike script remains as a regression probe

`spike-idle-tolerance.ts` is retained (not deleted) with:

- `INPUT_MODE=stream|queue` toggle demonstrating the broken and working patterns side-by-side.
- `PERMISSION_MODE`, `DANGEROUS_SKIP`, `MODEL`, `SET_MODE_ON_TURN2` envs so future investigations can probe the full configuration matrix the production worker uses.
- Updated header docstring documenting the SDK behavior found in this investigation.

**Rationale:** If a future SDK upgrade changes the `streamInput`/`endInput` behavior, this script is the shortest-path reproduction. Keeping it versioned in-repo is cheaper than re-deriving the finding.

## Risks / Trade-offs

- **[Risk]** `InputQueue`'s indefinite `await` keeps the SDK's internal `for await` suspended on an unresolved Promise for the whole session lifetime. If the SDK adds an internal timeout or heartbeat check on stdin writes, this could surface as a false-idle error.
  → **Mitigation:** The `spike-idle-tolerance.ts` covers 600s idle with the queue pattern; extend the idle period in a future spike run if the session idle distribution in production grows. No immediate mitigation required.

- **[Risk]** Hardening `OutputChannel` with a generator `finally` changes the semantics when multiple consumers attempt to iterate concurrently (nothing does this today, but future refactors might). Concurrent iteration would now reliably yield interleaved events to whichever consumer's resolver was active at push time.
  → **Mitigation:** Add a unit test that explicitly forbids concurrent iteration, or document the single-consumer-at-a-time invariant in the class JSDoc. Low risk.

- **[Risk]** `DEBUG_AGENT_SDK=true` in production could blow up log volume if accidentally left enabled, both in storage (Loki) and in SSE latency (stdout is synchronous). Mis-enabling in live tests also bloats `container.logs()` output.
  → **Mitigation:** Default to disabled everywhere; add a warning log line on session start when the flag is enabled ("[warn] DEBUG_AGENT_SDK is enabled — high log volume expected"); do NOT enable in CI by default.

- **[Trade-off]** Retaining the single long-lived-query shape means a CLI crash mid-session still kills the session (no per-turn restart safety net). This is unchanged from #776 and is acceptable given the CLI's stability profile.

- **[Trade-off]** The `OutputChannel` hardening adds complexity to a class that reads cleanly today. The alternative (make the consumer never abort mid-stream) is invasive across the SSE writer and the HTTP route layer.

## Migration Plan

1. **Land the fix** on a branch (no behavior flag — the old path is unrecoverable).
2. **Verify locally** by running `npm run test:live` against the new image; confirm `follow-up-prompts.live.test.ts` passes and that `prompts.live.test.ts` / other live tests still pass.
3. **Run the spike** in the Build-mode config with `INPUT_MODE=queue` to confirm no regression: `docker run ... -e INPUT_MODE=queue -e PERMISSION_MODE=bypassPermissions -e DANGEROUS_SKIP=true spike-idle-tolerance.ts`. Capture the output in the PR description.
4. **Deploy** through the normal release path. No data migration; no feature flag. All in-flight sessions terminate on worker restart (already true today).
5. **Post-deploy check** — observe Loki for no new `ProcessTransport is not ready for writing` errors on the worker `rx`/`error` log streams.

**Rollback**: Revert the PR. The pre-fix state is the known-broken #776 state; rollback re-introduces the bug and is only worthwhile if this fix has its own regressions that are worse.

## Confirmed root cause of the #776 regression slipping past CI

Investigation after this change was drafted confirms the regression merged green because **neither the spike nor the live tests were ever run in CI**:

- `src/Homespun.Worker/vitest.config.ts` explicitly excludes `**/live/**` from the default test runner.
- `src/Homespun.Worker/vitest.config.live.ts` documents this with a header comment stating the live tests are "NOT intended to run in CI".
- The worker CI job (`.github/workflows/ci.yml` → `worker-build` → `npm run test:coverage`) uses the default config, so all `*.live.test.ts` files are skipped.
- No workflow invokes `npm run test:live`, and no workflow invokes `scripts/spike-idle-tolerance.ts` — the spike is not referenced in `package.json`'s `scripts` either.
- The only tests that ran against the refactor were unit tests using `tests/Homespun.Worker/helpers/mock-sdk.ts`, which evidently does not reproduce the SDK's `endInput()`-on-iterable-exhaustion contract, so the broken path was never exercised.

**Implication for this change:** pinning the SDK minor version is not necessary — the SDK behavior was the same before and after; the gap was in test coverage. Two corrective actions follow from this:

1. The worker's mock SDK SHOULD enforce the same contract the real SDK enforces (i.e. throw on `streamInput` / control calls after the initial iterable has been exhausted), so unit tests catch any future reintroduction of the `onceIterator` pattern.
2. The live-test suite SHOULD run at least post-merge on `main`, or nightly, so follow-up regressions are caught within a day rather than waiting for manual execution.

Both are included as tasks in `tasks.md`.

## Open Questions

- **Do we need concurrent-consumer safety on `OutputChannel`?** Current code has exactly one consumer at any time. If a future refactor (e.g. attaching a side-channel metrics consumer) violates this, the hardening's semantics need revisiting. Deferred.
- **Should we emit `DEBUG_AGENT_SDK` entries for A2A control events (question_pending, plan_pending, workflow_complete)?** They are worker-internal synthesis, not SDK-boundary messages. Current plan: no. Revisit if diagnosing a pending-interaction bug proves hard without them.
- **Should live tests be required for PR merge, or post-merge only?** Required-for-PR is a strong signal but costs inference $$ and wall-clock on every push. Post-merge / nightly catches regressions within hours and is cheaper. Leaning post-merge; confirm with the team.
