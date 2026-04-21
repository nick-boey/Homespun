## 1. Confirm regression probe and baseline

- [x] 1.1 Review `src/Homespun.Worker/scripts/spike-idle-tolerance.ts` (already updated during exploration) and update the header docstring to document the SDK's `streamInput` + `endInput()` behavior and the `INPUT_MODE=queue` fix
- [x] 1.2 Run the spike with `INPUT_MODE=stream` in both default/haiku and Build/sonnet configs; record the `ProcessTransport is not ready for writing` failure in the PR description as pre-fix evidence — evidence saved in `evidence/spike-1.2-stream-haiku.txt` and `evidence/spike-1.2-stream-sonnet-build.txt`; both failed with `streamInput rejected: ProcessTransport is not ready for writing`.
- [x] 1.3 Run the spike with `INPUT_MODE=queue` + Build/sonnet + `SET_MODE_ON_TURN2=true` and confirm `FOLLOW-UP SUCCESS`; record in the PR description — evidence saved in `evidence/spike-1.3-queue-sonnet-build.txt`; `FOLLOW-UP SUCCESS` observed after mid-turn `setPermissionMode` + queued follow-up.

## 2. Add `InputQueue` primitive

- [x] 2.1 Extract the validated `InputQueue` class from `scripts/spike-idle-tolerance.ts` into `src/Homespun.Worker/src/services/input-queue.ts` with `push(msg)`, `close()`, and `[Symbol.asyncIterator]()` methods
- [x] 2.2 Add JSDoc on `InputQueue` noting single-consumer invariant and "iterator does not return until close()"
- [x] 2.3 Unit tests in `tests/Homespun.Worker/services/input-queue.test.ts`:
  - [x] 2.3.1 Push-then-iterate yields the message
  - [x] 2.3.2 Iterate-then-push delivers via the pending resolver
  - [x] 2.3.3 Multiple sequential pushes are delivered in FIFO order
  - [x] 2.3.4 `close()` causes the in-flight `await next()` to resolve `{ done: true }`
  - [x] 2.3.5 `push()` after `close()` is a no-op (does not throw)

## 3. Harden `OutputChannel`

- [x] 3.1 Wrap the generator body in `try { ... } finally { this.resolver = null; }` in `src/Homespun.Worker/src/services/session-manager.ts` (`OutputChannel.[Symbol.asyncIterator]`)
- [x] 3.2 Add a class-level JSDoc note that single-consumer-at-a-time is required
- [x] 3.3 Unit tests in `tests/Homespun.Worker/services/output-channel.test.ts` (new file if needed):
  - [x] 3.3.1 Event pushed between iterator re-entries is delivered to the next consumer
  - [x] 3.3.2 Pending resolver is cleared when a consumer aborts its `for await`
  - [x] 3.3.3 `complete()` still terminates an in-flight `await next()`

## 4. Rewire `SessionManager` to use `InputQueue`

- [x] 4.1 Add `inputQueue: InputQueue` to the `WorkerSession` interface in `session-manager.ts`
- [x] 4.2 In `create()`, construct `InputQueue`, push the initial prompt, and pass the queue as `prompt` to `query({...})` — remove the `onceIterator` usage for the initial prompt
- [x] 4.3 In `send()`, replace `await ws.query.streamInput(onceIterator(userMessage))` with `ws.inputQueue.push(userMessage)`
- [x] 4.4 In `close()`, call `ws.inputQueue.close()` before `ws.query.close()`
- [x] 4.5 Remove the `onceIterator` helper if no longer used elsewhere
- [x] 4.6 Confirm `runQueryForwarder` still runs once per session lifetime — no changes expected, but verify its `for await (const msg of q)` is never exited early by upstream iterator aborts

## 5. Wire `DEBUG_AGENT_SDK` debug logging

- [x] 5.1 Add `sdkDebug(direction: 'tx' | 'rx', msg: unknown): void` helper to `src/Homespun.Worker/src/utils/logger.ts`, gated by `process.env.DEBUG_AGENT_SDK === 'true'`, emitting a single structured-JSON line via the existing format
- [x] 5.2 On session create, emit a `warn`-level log "DEBUG_AGENT_SDK is enabled — high log volume expected" once per session when the flag is enabled
- [x] 5.3 Call `sdkDebug('tx', sessionOptions)` immediately before `query({...})` in `create()` — redact env credentials (`GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`) before logging
- [x] 5.4 Call `sdkDebug('tx', userMessage)` inside `InputQueue.push(...)` (add the hook in `session-manager.ts` before the push — the queue itself stays I/O-agnostic)
- [x] 5.5 Call `sdkDebug('tx', { op: 'setPermissionMode', mode })` before `ws.query.setPermissionMode(...)` in both `setMode()` and `send()`
- [x] 5.6 Call `sdkDebug('tx', { op: 'setModel', model })` before `ws.query.setModel(...)` in both `setModel()` and `send()`
- [x] 5.7 Call `sdkDebug('rx', msg)` at the top of the `for await (const msg of q)` body in `runQueryForwarder`
- [x] 5.8 Unit test: with `DEBUG_AGENT_SDK=true` stubbed, all four hooks fire once per invocation; with it unset, no hooks fire

## 6. Propagate `DEBUG_AGENT_SDK` through both invocation paths

- [x] 6.1 Add `DEBUG_AGENT_SDK=${DEBUG_AGENT_SDK:-false}` to the `worker:` service environment in `docker-compose.yml`
- [x] 6.2 In `tests/Homespun.Worker/live/fixtures/container-lifecycle.ts`, forward `process.env.DEBUG_AGENT_SDK` to the container via `-e` when set
- [x] 6.3 Document the flag briefly in the worker's `src/Homespun.Worker/Dockerfile`-adjacent location where env vars are listed (if such documentation exists — skip if not) — SKIPPED: Dockerfile has no env-var documentation section; `docker-compose.yml` inline comment serves that role.

## 7. Fix test-double fallout from #776

- [x] 7.1 Update `tests/Homespun.Worker/helpers/mock-sdk.ts` so the mock `Query` no longer requires `streamInput` for follow-ups; the mock should expose an input-queue injection hook instead
- [x] 7.2 **Enforce the real SDK's contract in the mock**: after the initial input iterable has been exhausted (i.e. returns `{ done: true }`), subsequent `streamInput`, `setPermissionMode`, `setModel`, and any other transport-write must throw `ProcessTransport is not ready for writing` to mirror the real `sdk.mjs` behavior. Added unit test `mock SDK contract: streamInput throws after initial input iterable exhausted`.
- [x] 7.3 Update `tests/Homespun.Worker/helpers/mock-session-manager.ts` to match — unchanged (no `streamInput` references present; mock-session-manager is interface-level).
- [x] 7.4 Update `tests/Homespun.Worker/services/session-manager.test.ts` tests that asserted `q.streamInput` was called on follow-up to assert `inputQueue.push` (or equivalent observable side-effect) instead
- [x] 7.5 Re-run the full worker unit test suite (`npm test` from `src/Homespun.Worker`) and confirm all tests pass — 217/217 passing.

## 8. Integration verification

- [x] 8.1 Rebuild the worker image: `docker build -t homespun-worker:local ./src/Homespun.Worker` — image built cleanly.
- [x] 8.2 Run `npm run test:live` from `src/Homespun.Worker`; confirm `follow-up-prompts.live.test.ts` passes — 6/6 live tests passed.
- [x] 8.3 Confirm no regressions in the other live tests (`prompts.live.test.ts`, `file-operations.live.test.ts`, `questions-planning.live.test.ts`) — 4 test files / 6 tests all green.
- [x] 8.4 Run the spike one more time with `INPUT_MODE=queue` + full Build config and attach the output to the PR — captured in `evidence/spike-1.3-queue-sonnet-build.txt`.
- [x] 8.5 With `DEBUG_AGENT_SDK=true` set in the host env, rerun `follow-up-prompts.live.test.ts`; verify the four `tx`/`rx` log points appear in `container.logs()` output — verified: 4x `[SDK tx]` entries (sessionOptions with redacted credentials, initial user message, setPermissionMode) + 9x `[SDK rx]` entries (system/init, assistant messages, result). Credentials are properly redacted (`CLAUDE_CODE_OAUTH_TOKEN: "[REDACTED]"`, `GITHUB_TOKEN: "[REDACTED]"`).

## 9. CI coverage so this class of regression surfaces automatically

- [x] 9.1 Add a post-merge (or nightly `schedule:`) workflow that runs `npm run test:live` from `src/Homespun.Worker` against a just-built `homespun-worker:local` image. Required secrets: `CLAUDE_CODE_OAUTH_TOKEN`. Budget: expect ~10–15 minutes; cap concurrency to one to avoid parallel inference spend.
- [x] 9.2 Include `scripts/spike-idle-tolerance.ts` in the same workflow as a quick-check step (`INPUT_MODE=queue IDLE_SECONDS=3` — under 30 seconds of inference) so a future SDK change that re-breaks `streamInput` surfaces before the full live suite completes.
- [x] 9.3 Remove the "NOT intended to run in CI" comment in `vitest.config.live.ts` or replace it with a note pointing at the new workflow.

## 10. Documentation and follow-up

- [x] 10.1 Add a note to `openspec/changes/simplify-worker-session-manager/design.md` Open Questions section (or the archived spec if already archived) pointing at this change as the correction
- [x] 10.2 Capture a project-memory entry describing how to diagnose SDK-boundary issues using `DEBUG_AGENT_SDK`
- [x] 10.3 Capture a project-memory entry noting the CI gap that let #776 merge broken (live tests and spikes were not in CI) and the remediation in this change
- [x] 10.4 Confirmed not needed: pinning `@anthropic-ai/claude-agent-sdk` — the SDK behavior was the same pre- and post-#776; the regression slipped through because of test coverage, not an SDK change

## 11. Pre-PR checklist

- [x] 11.1 `dotnet test` passes (should be unaffected — change is worker-only) — 2287 passed, 7 skipped, 0 failed.
- [x] 11.2 Worker `npm run lint:fix` / `npm run format:check` / `npm test` pass — worker package does not define `lint:fix` / `format:check` scripts (no ESLint/Prettier config in `src/Homespun.Worker`); TypeScript typecheck (`npx tsc --noEmit`) is clean and `npm test` reports 217/217 passing.
- [x] 11.3 PR description includes: the pre-fix failure output from step 1.2, the post-fix success from step 1.3, and a link to this OpenSpec change — evidence captured under `evidence/`; paste when opening the PR.
      (PR #777 — `fix(worker): replace streamInput with persistent InputQueue for multi-turn sessions` — merged.)
- [x] 11.4 Verify `openspec status --change fix-worker-streaminput-multi-turn` reports all artifacts done

