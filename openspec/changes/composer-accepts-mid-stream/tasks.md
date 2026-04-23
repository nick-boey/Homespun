# Tasks: Composer accepts mid-stream messages

**Input**: `proposal.md` + `specs/mid-stream-messaging/spec.md`.
**Status**: Proposed — independent of other changes. Small scope.

## Path Conventions

| Concern | Path |
|---------|------|
| Composer gate | `src/Homespun.Web/src/routes/sessions.$sessionId.tsx` |
| Composer component | `src/Homespun.Web/src/features/sessions/components/chat-input.tsx` |
| Fixture envelopes | `src/Homespun.Web/src/features/sessions/fixtures/envelopes.ts` (new file from `chat-assistant-ui` OR existing location) |
| Worker (verify-only) | `src/Homespun.Worker/src/services/session-manager.ts` |

---

## Phase 1: Verify worker + server paths

- [ ] 1.1 Read the worker's `session-manager.ts` comment block at lines 742–745 and the `fix-worker-streaminput-multi-turn` OpenSpec archive (if archived) to confirm the contract. Link in the PR.
- [ ] 1.2 Run the existing worker tests covering mid-run `InputQueue.push`. If no such test exists, add one: construct a session, start iterating, `push` during iteration, assert the next iterated message matches.
- [ ] 1.3 Confirm `ClaudeCodeHub.SendMessage` and `DockerAgentExecutionService.SendMessageAsync` have no `isRunning`-equivalent guard. Grep for state checks in both files; if present, document and decide whether to keep.

## Phase 2: Client unblock

- [ ] 2.1 In `src/Homespun.Web/src/routes/sessions.$sessionId.tsx`, remove `isProcessing` from the composer's `disabled` expression at line 412. Keep `!isConnected || !isJoined` and the `pendingPlan` / `pendingQuestion` guards (if present in current code) unchanged.
- [ ] 2.2 Verify the `handleSend` path (around line 152) routes:
  - to `reject(text)` when `hasPendingPlan` is true (preserve existing behaviour),
  - to `SendMessage(text, mode, model)` otherwise.
- [ ] 2.3 Add a route-level test: mount the session page with `session.status === 'running'`; assert the composer is enabled; simulate a submit; assert the client invokes `SendMessage` (mocked hub client).

## Phase 3: Queued-state visual indication

- [ ] 3.1 Derive a "queued" flag per user message in the reducer-or-view-layer: a user message is "queued" iff `session.status === 'running'` at the time of its arrival AND no subsequent `RUN_STARTED` has been observed.
  - Simplest derivation: inside the message view layer, compare the message's position in the list to the most recent `RUN_STARTED` envelope's seq. If the message's arrival seq > last RUN_STARTED seq and the session is running, it's queued.
- [ ] 3.2 Render a subtle clock icon (or faded styling) on queued user messages. Use a semantic aria-label like `aria-label="queued for processing"`.
- [ ] 3.3 Verify the indicator clears the moment a `RUN_STARTED` arrives after the queued message.
- [ ] 3.4 Storybook story: `ChatSurface — typing during assistant run` fixture that renders an in-progress run, a queued user message, and the subsequent RUN_STARTED that clears the indicator.

## Phase 4: Optional — Stop button + interrupt

**Descope to a follow-up if the worker wiring is non-trivial.**

- [ ] 4.1 Add a new hub method `InterruptSession(sessionId)` on `ClaudeCodeHub`. Handler calls a new `ISessionService.InterruptSessionAsync(sessionId)`.
- [ ] 4.2 Worker HTTP: add `POST /api/sessions/:id/interrupt`. Handler calls `ws.query.interrupt()` and logs the outcome. Returns 200 on success, 404 if unknown session, 409 if not currently running.
- [ ] 4.3 Server `DockerAgentExecutionService.InterruptSessionAsync` POSTs to the worker's interrupt endpoint (mirror the answer/approve plumbing).
- [ ] 4.4 Client: `Stop` button in the composer area, visible only while `isProcessing`. OnClick invokes `InterruptSession`.
- [ ] 4.5 Integration test via `HomespunWebApplicationFactory`: invoke Interrupt; verify the next A2A event from the worker reflects the interrupt (likely `RunFinished` with a cancelled flag or an error; confirm via the translator).

## Phase 5: Regression pass

- [ ] 5.1 `npm run test:e2e` — verify the golden paths still work.
- [ ] 5.2 Manual via `dotnet run --project src/Homespun.AppHost --launch-profile dev-live`: start a long-running agent task, type a follow-up message while it's thinking, verify it appears immediately with the queued indicator, verify it's processed after the current turn ends.
- [ ] 5.3 Verify the `reject-with-feedback` path still works when a plan is pending.
- [ ] 5.4 Verify traces: the mid-run user message span parents under the correct server activity; no orphaned spans.

## Phase 6: Close-out

- [ ] 6.1 Pre-PR checklist (dotnet test, lint/fix, typecheck, test, test:e2e, build-storybook).
- [ ] 6.2 Update `src/Homespun.Web/CLAUDE.md` (if a chat-surface guidance section exists post-`chat-assistant-ui`): one sentence noting that the composer accepts mid-run input and messages queue on the worker's `InputQueue`.
- [ ] 6.3 Consider a small docs note in `docs/session-events.md` (if it exists) describing the queued-then-consumed flow for operator troubleshooting.
