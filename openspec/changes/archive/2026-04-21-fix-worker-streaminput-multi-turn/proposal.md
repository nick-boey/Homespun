## Why

The `follow-up-prompts.live.test.ts` worker live test fails after PR #776. Empirical testing (reproduced with `scripts/spike-idle-tolerance.ts`) confirms the SDK's `streamInput()` method always calls `transport.endInput()` after consuming its input iterable — which closes stdin to the CLI process and causes the CLI to exit after the first turn. The single long-lived `query()` pattern #776 introduced is therefore fundamentally incompatible with multi-turn conversations: the `onceIterator(initialPrompt)` yields and returns, the SDK closes stdin, and the next `q.streamInput(...)` or `q.setPermissionMode(...)` call fails with `ProcessTransport is not ready for writing`. Multi-turn agent sessions are broken in production, not just in tests.

A debug-logging facility is also missing: there is no way to inspect the exact SDK inputs and outputs when an agent session misbehaves, whether the worker runs under docker compose (PLG stack) or standalone (live tests).

## What Changes

- Replace the `onceIterator(initialPrompt)` + `q.streamInput(onceIterator(followup))` pattern in `session-manager.ts` with a persistent `InputQueue` that:
  - Serves as the initial `prompt` AsyncIterable passed to `query({...})`
  - Never returns on its own — stays `await`-suspended between messages
  - Receives follow-up messages via `queue.push(...)`, never `q.streamInput(...)`
  - Is closed only when the session itself is closed
- Convert `runQueryForwarder` into a single lifetime-scoped drain over the SDK's `Query` — it already is, but the early-return from `streamSessionEvents` on `result` must not propagate into the SDK iterator and mark it done.
- Harden `OutputChannel` so pushes between per-turn iterator re-entries are never lost:
  - Clear `this.resolver` in a `finally` block of the `[Symbol.asyncIterator]` generator
  - Guarantee event delivery across multiple sequential `for await` consumers on the same channel
- Add a `DEBUG_AGENT_SDK` environment-gated logging channel with four capture points in `session-manager.ts`:
  1. `tx` — full `sessionOptions` just before `query({...})`
  2. `tx` — user message payload on every `InputQueue.push(...)`
  3. `tx` — control-request args for `setPermissionMode` / `setModel`
  4. `rx` — every raw SDK message inside `runQueryForwarder`'s `for await`
- Wire `DEBUG_AGENT_SDK` through:
  - `docker-compose.yml` worker service environment (read from host env, default `false`)
  - Live-test container fixture in `tests/Homespun.Worker/live/fixtures/container-lifecycle.ts` (forward host `DEBUG_AGENT_SDK` through `-e`)
- Update the `scripts/spike-idle-tolerance.ts` header docstring to reflect the new understanding: `streamInput()` is effectively single-use per `Query`, and the persistent-queue pattern is the correct shape. Preserve the `INPUT_MODE=queue` / `INPUT_MODE=stream` toggle so the spike remains a regression probe.

## Capabilities

### New Capabilities

_None — this change modifies existing behavior rather than introducing a new capability._

### Modified Capabilities

- `claude-agent-sessions`: The send-follow-up-message behavior changes from "call `q.streamInput()` on the live query" to "push into the session's persistent input queue"; adds a new `DEBUG_AGENT_SDK` observability requirement that captures every SDK-boundary message when enabled.

## Impact

- **Code changed**
  - `src/Homespun.Worker/src/services/session-manager.ts` — new `InputQueue` class; `create()` uses it as the prompt; `send()` pushes into it instead of `streamInput`; `close()` closes it; optional SDK-debug calls at the four capture points
  - `src/Homespun.Worker/src/services/sse-writer.ts` — no behavior change expected, but verify that the early-return on `result` does not propagate iterator-done into the SDK query
  - `src/Homespun.Worker/src/utils/logger.ts` — new `sdkDebug(direction, msg)` helper gated by `DEBUG_AGENT_SDK`
  - `docker-compose.yml` — pass `DEBUG_AGENT_SDK` into the `worker:` service env
  - `tests/Homespun.Worker/live/fixtures/container-lifecycle.ts` — forward host `DEBUG_AGENT_SDK` into the container
  - `src/Homespun.Worker/scripts/spike-idle-tolerance.ts` — updated docstring (code already updated during exploration)
- **Tests changed**
  - `tests/Homespun.Worker/services/session-manager.test.ts` — tests in `simplify-worker-session-manager` that asserted `q.streamInput()` as the send mechanism need to assert `InputQueue.push()` instead; the mock SDK no longer needs `streamInput` behavior
  - `tests/Homespun.Worker/helpers/mock-sdk.ts` — drop `streamInput` mock, ensure the mock query's AsyncIterable consumes from a queue
  - New unit tests for `InputQueue` (never-returning iterator, push/pull ordering, close semantics)
  - New unit tests for `OutputChannel` proving no events are lost across sequential iterator re-entries
  - `tests/Homespun.Worker/live/tests/follow-up-prompts.live.test.ts` becomes the integration check — expected to pass after the fix
- **SDK contract**
  - Relies on `@anthropic-ai/claude-agent-sdk@^0.2.81` behavior observed in `sdk.mjs`: `streamInput` always calls `endInput()` at the tail, and the initial `AsyncIterable` prompt is internally consumed via the same `streamInput` routine. The spike script becomes the regression detector if this changes.
- **Breaking changes**
  - None externally visible — the HTTP API shape is unchanged. Internal test assertions that probed `q.streamInput` need to move to `InputQueue.push`.
- **Rollback posture**
  - Cleanly revertible by restoring the `onceIterator` + `q.streamInput` pattern, but that re-introduces the known-broken behavior.
- **Confirmed: why #776 merged green**
  - CI's worker job (`worker-build` in `.github/workflows/ci.yml`) runs `npm run test:coverage` which uses `vitest.config.ts` — this config explicitly excludes `**/live/**`. No workflow invokes `npm run test:live` or `scripts/spike-idle-tolerance.ts`. The live test that would have caught the regression, and the spike that supposedly validated the refactor, were never run in CI or (apparently) locally. The unit-test suite passed because `tests/Homespun.Worker/helpers/mock-sdk.ts` does not reproduce the SDK's `endInput()`-on-iterable-exhaustion contract. This is a test-coverage gap, not an SDK-version issue; pinning the SDK is not required.
- **Follow-ups included in this change (see tasks.md)**
  - Make the mock SDK enforce the real SDK's `endInput()` contract so unit tests catch any reintroduction of the `onceIterator` pattern.
  - Wire the live-test suite into a post-merge or nightly CI job so follow-up regressions on `main` surface within hours rather than on the next manual run.
  - Add a correction note to `openspec/changes/simplify-worker-session-manager/design.md` (or the archived spec if already archived) pointing at this change.
