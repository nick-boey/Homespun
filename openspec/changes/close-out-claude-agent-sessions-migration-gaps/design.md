## Context

Seven follow-up issues (FI-1…FI-7) were left open after the brownfield migration of the Claude Agent Sessions feature. Today they sit in flat-list form on the OMfXwp verify, with no shared sequencing or technical alignment. Five of the seven are server-side bug fixes; two (FI-1, FI-2) are pure test-coverage work; one (FI-7) is a feature requiring architectural decisions. This design exists for one reason: **FI-7 needs technical alignment before code lands**, and bundling the rest under it lets us drive a single coherent close-out PR sequence rather than seven separate spec-less remediations. The smaller fixes (FI-3 through FI-6) are mostly mechanical — design notes for them are short and serve as a contract for the spec deltas.

The change spans the server slice (`Features/ClaudeCode/`), the worker (`src/Homespun.Worker/`), the web slice (`features/sessions/`), and the shared contracts (`Homespun.Shared`). Phasing the implementation is necessary so each PR stays reviewable and so the user can stop after any phase if priorities shift.

## Goals / Non-Goals

**Goals:**
- Close OMfXwp by landing remediations for FI-1 through FI-7 under a single coherent change.
- Phase the work so each PR is independently reviewable and revertible.
- Resolve the architectural questions for FI-7 (summarise vs trim, where the trigger lives, how the user is informed) **before** implementation tasks land.
- Add regression tests for every behavioural change so the migration gaps cannot recur.

**Non-Goals:**
- Restructuring the agent-execution stack (Docker mode, single-container mode, ACA mode) — only the bug surfaces inside `DockerAgentExecutionService` are touched.
- Changing the AG-UI envelope shape, A2A protocol, or worker SDK plumbing.
- Multi-user / authn (tracked under `xJ1xoN`).
- Token-budget enforcement at the API boundary — context management is a mitigation for *long sessions*, not a per-call quota.
- Backfilling test coverage outside the worker (`Homespun.Worker/`) module — FI-2 is scoped to that module only.

## Decisions

### D1 — FI-3: Carry persisted `Mode`/`Model` on the in-memory session record, not via lookup-on-read

**Decision:** Add `Mode` and `Model` fields to `DockerAgentExecutionService.DockerSession` and to the matching `ClaudeSession` shared model wherever both are written together. On creation, populate them from the request DTO (or the project default). On Docker-mode container recovery (`TryRecoverOrRemoveExistingContainerAsync` and the `RegisterDiscoveredContainer` path), read the worker's `/sessions/active` response which already exposes `Mode` and `Model` (`ActiveSessionResponse.Mode`/`Model`). When the worker did not return a value, fall back to `SessionMetadataStore.GetBySessionIdAsync` for the persisted hints.

**Alternative considered:** keep the record minimal and look up `SessionMetadataStore` on every `GetSessionStatusAsync` / `ListSessionsAsync` call. **Rejected** — `ListSessionsAsync` is on the hot path of the session list page; a per-call disk read is unjustified when the values change rarely.

**Why:** the bug is purely "we know the values, we threw them away". Storing them in the record is the smallest surface-area fix, and the SessionMetadataStore already exists as the durable source of truth.

### D2 — FI-4: Awaited bounded-timeout dispatch, not a correlation handle

**Decision:** Replace the fire-and-forget `Task.Run` in `SessionsController.Create` with an `await` on `sessionService.SendMessageAsync` wrapped in a `CancellationTokenSource(TimeSpan.FromSeconds(30))`. On timeout, return `202 Accepted` with the session id and let the SignalR stream surface the result. On `Exception`, return `500 Internal Server Error` with the session id so the client can retry or surface the failure.

**Alternative considered:** correlation-id pattern (return id immediately, broadcast errors via `BroadcastSessionError`). **Rejected for now** — adds a hub method, a client subscription path, and a new error envelope shape, all to handle a path that succeeds in well under 30s in normal operation. The bounded-await is a strict improvement over the current behaviour and keeps the public contract simple. If session creation later grows past 30s reliably, we revisit.

**Why:** dispatch failures today are completely silent to clients without an active hub connection. A bounded await closes the gap with one method change and zero new public surface.

### D3 — FI-5: `TryUpdate` over manual `ContainsKey` + indexer

**Decision:** Replace the body of `ClaudeSessionStore.Update`:

```csharp
// before
if (!_sessions.ContainsKey(session.Id)) return false;
_sessions[session.Id] = session;
return true;

// after
return _sessions.AddOrUpdate(
    session.Id,
    _ => throw new InvalidOperationException("session must be added first"),
    (_, _) => session
) is not null;
```

…or use `TryGetValue` + `TryUpdate` for an explicit compare-and-swap. The choice is whichever passes the concurrency stress test cleanly.

**Alternative considered:** lock-free vs `lock (_lock)`. **Rejected** — the dictionary already uses lock-striping; an external lock is strictly worse for contention.

**Why:** the existing pattern can re-insert a session that was just removed by a concurrent thread. The atomic primitive is one line of code.

### D4 — FI-6: Plan files are owned by the session, not the tool that wrote them

**Decision:** Treat `PlanFilePath` as a side-effect of the session's lifetime. Introduce a small `IPlanArtefactStore` (or extend `MessageCacheStore`) that:
1. Records every plan file that gets written, keyed by session id.
2. Exposes `RemoveForSessionAsync(sessionId)` called from `SessionLifecycleService.StopSessionAsync` and from `DockerAgentExecutionService.RestartContainerAsync` / container-removal paths.
3. Exposes `TryReadAsync(path)` that returns `null` (not an exception) when the file is missing so the UI can degrade gracefully.

On the web side, `PlanApprovalPanel` and `usePlanFiles` consume `null` content and render a "plan file no longer available" state instead of throwing.

**Alternative considered:** delete the plan file inline in `ToolInteractionService` whenever the plan is approved/rejected. **Rejected** — that doesn't cover the cases the issue describes (worker container removed mid-session, server crash before approval).

**Why:** the existing leak is that no actor owns the cleanup. Putting that ownership on the session lifetime is the natural place.

### D5 — FI-1: Test against the existing mock server, not Docker-mode

**Decision:** All six new specs use the standing `webServer` config in `playwright.config.ts` which already starts the mock-mode AppHost. No specs require a real worker. The "stream a message" spec uses the canned mock A2A events; the "approve a plan" spec asserts UI-side behaviour against the mock plan flow; etc.

**Alternative considered:** spin up `dev-windows` profile (single live worker container) for each spec. **Rejected** — adds Docker as a CI dependency and gates merges on container-build wallclock; the gain is minimal for what is essentially UI-flow regression testing.

**Why:** Constitution IV calls for `npm run test:e2e` to exercise shipped slices in CI. The mock-server path already runs in CI on every PR; using it keeps the new specs cheap to author and run.

### D6 — FI-2: Coverage gap analysis before authoring tests

**Decision:** Run `npm run test:coverage` in `tests/Homespun.Worker/` first to identify where the current 41 test files leave gaps. Author tests only for the gaps. Drop `workflow-tools.ts` from the acceptance list (file no longer exists). Track the run on PR description so the ≥60% module target is auditable.

**Alternative considered:** rewrite all worker tests against a fresh fixture. **Rejected** — wasteful and out of scope.

### D7 — FI-7: Threshold-triggered summarise, with trim as the safety fallback

This is the one decision that warrants real analysis.

**Decision:** Run a token-usage observer at the server. After every `RunFinished`, compute `cumulative_input_tokens / context_window_for_model`. When the ratio crosses **0.75**, broadcast a `SessionContextManaging` AG-UI custom event and start a **summarise** turn: synthesise a "[CONTEXT SUMMARY]" prompt that asks the agent to summarise the conversation and call `ClearContextAndStartNew` with that summary as the initial message of the new conversation. If the summarise turn itself fails or the post-summary ratio still exceeds 0.9, fall back to **trim**: drop the oldest non-system messages from the AG-UI replay until the ratio drops below 0.5.

Configuration:
- `Project.ContextManagement.Mode` — `Auto` (default) | `Off` | `Summarise` | `Trim` | `Manual`
- `Project.ContextManagement.SummariseThreshold` — float in (0, 1), default 0.75
- `Project.ContextManagement.TrimFloor` — float in (0, 1), default 0.5

User signal:
- A new AG-UI event `SessionContextManaging { strategy: "summarise" | "trim", reason: "threshold-exceeded" | "summarise-failed" }` is broadcast at the start.
- A `SessionContextManaged { strategy, prevRatio, newRatio, droppedTurnCount }` event closes the operation.
- Both events are persisted to the A2A event log so refresh/replay produces the same sequence.

**Alternatives considered:**
- *Trim only*: simpler, but loses semantic context. Useful as a fallback, not a primary.
- *Summarise only*: cleaner UX, but if the summariser itself fails (rate limit, context exhausted), we have no escape hatch.
- *Hand off to the SDK's built-in `compactionStrategy`*: the SDK does not currently expose user-tunable thresholds and runs at a different layer; we'd lose the per-project knob.
- *Trigger at a fixed turn count*: doesn't account for tool-heavy turns that consume tokens disproportionately.

**Why this design:** ratio-based triggering reacts to the actual signal that matters; summarise-then-trim provides a graceful-degradation ladder; per-project config matches how Homespun already exposes Plan/Build defaults; explicit AG-UI events keep the live==replay invariant.

**Open in this design:** the exact prompt text used for the summarise turn (FI-7 implementation tasks will iterate on it), and whether the trim path should preserve the *first* user turn (typically the framing) — leaning yes, but defer to implementation.

## Risks / Trade-offs

- **[FI-3 risk] Worker `Mode`/`Model` may lag the persisted hint after a worker restart that drops the values** → mitigation: read both, prefer worker-reported values, fall back to metadata; log when the two disagree so we can audit drift.
- **[FI-4 risk] 30s bounded await may fire spuriously on first-cold-start** → mitigation: tune via `SessionEvents:DispatchTimeoutSeconds` config if needed; default 30s matches existing test fixtures.
- **[FI-7 risk] Summarise turn itself blows the context window** → mitigation: explicit fall-through to trim; trim path is bounded by `TrimFloor` and never deletes system or first-user turns.
- **[FI-7 risk] Per-project config drift between server and worker** → mitigation: server is authoritative; worker only reports token usage. No worker-side config knob is added.
- **[FI-1 risk] Mock-mode e2e drift** → mitigation: each spec asserts on AG-UI envelope content the mock server already emits; we don't add new mock fixtures.
- **[Scope risk] Bundling 7 issues into one change** → mitigation: phasing in `tasks.md`. Each phase is its own PR; later phases can be deferred without unwinding earlier ones.

## Migration Plan

This is a behavioural close-out, not a data migration. The phases below match `tasks.md`:

1. **Phase 1** — FI-3, FI-4, FI-5: server-side bug fixes. Deployable independently.
2. **Phase 2** — FI-6: plan-file lifecycle. Server + web changes; tests cover both.
3. **Phase 3** — FI-1: 6 Playwright specs. Test-only; no production code.
4. **Phase 4** — FI-2: worker test gap fill. Test-only; no production code.
5. **Phase 5** — FI-7: context management. Largest blast radius; lands last so the earlier fixes are already in production when this ships.

Rollback: each phase is a separate PR; revert the offending PR. FI-7 in particular is gated by `Project.ContextManagement.Mode = Off` for any project that hits trouble.

## Open Questions

- **FI-7 token accounting source**: the SDK reports `usage.input_tokens` per `result` message. Does that include cached input? Need to confirm before computing ratio against `context_window_for_model`.
- **FI-7 model context windows**: where does the canonical `context_window` per model live? `IModelCatalogService` returns model ids and tier aliases but not window sizes today. May need to extend the model catalog DTO. Defer to FI-7 implementation tasks.
- **FI-1 selectors**: Playwright specs need stable test ids on the existing components. Some panels (`PlanApprovalPanel`, Q&A panel) may need `data-testid` additions — track per spec when that is the case.
