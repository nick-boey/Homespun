---
description: "Phased task list for close-out-claude-agent-sessions-migration-gaps"
---

# Tasks: Close Out Claude Agent Sessions Migration Gaps

> **Phasing.** Each numbered phase is a single PR. Phases can be merged in order — earlier phases do not depend on later ones, but later phases assume the earlier ones are in. Phases 3 and 4 are test-only and can be reordered with each other if scheduling demands.
>
> **Pre-PR gate.** Every phase clears the full Constitution IV gate before opening a PR: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook`, and `npm run generate:api:fetch` if any controller signature changed.

---

## 1. Phase 1 — Server-side bug close-out (FI-3, FI-4, FI-5)

**Scope:** atomic close-out of three short server-side bugs. Net diff target ≤ 400 LOC including tests.

### FI-3 Persisted Mode/Model on recovery

- [x] 1.1 Write failing test in `tests/Homespun.Tests/Features/ClaudeCode/DockerAgentExecutionServiceTests.cs` asserting `ListSessionsAsync` returns the persisted `Mode`/`Model` for an in-memory session created with `Mode = Plan`, `Model = "opus"` (currently fails because of hardcoded `Build`/`sonnet`).
- [x] 1.2 Write failing test asserting `GetSessionStatusAsync` returns the persisted `Mode`/`Model` for the same session.
- [x] 1.3 Write failing recovery test in `ContainerRecoveryHostedServiceTests` (or equivalent) asserting that a recovered session surfaces the worker's reported `Mode`/`Model` via `ActiveSessionResponse`, falling back to `SessionMetadataStore` when the worker has no value.
- [x] 1.4 Extend `DockerAgentExecutionService.DockerSession` record with `Mode` and `Model` fields. Populate them on every code path that constructs a `DockerSession` (`StartContainerAsync`, `RestartContainerAsync`, recovery registration).
- [x] 1.5 Replace the hardcoded `SessionMode.Build, "sonnet"` literals in `GetSessionStatusAsync` (~L1236) and `ListSessionsAsync` (~L1299) with the values from `DockerSession`. Remove the `// TODO: Store actual mode/model` comments.
- [x] 1.6 Wire `SessionMetadataStore` lookup into the recovery code path (`RegisterDiscoveredContainer` + the discovery hosted service) so persisted hints fill in any value the worker did not report.
- [x] 1.7 Confirm `npm run generate:api:fetch` produces no diff (no controller signature change here — `AgentSessionStatus` is server-internal) and run `dotnet test` until all FI-3 tests pass.

### FI-4 Surface POST /api/sessions dispatch failures

- [x] 1.8 Add `SessionEvents:DispatchTimeoutSeconds` (default 30) to `appsettings.json` + `SessionEventsOptions` POCO + DI binding.
- [x] 1.9 Write failing test in `tests/Homespun.Api.Tests/Features/SessionsApiTests.cs` asserting that when the worker rejects the initial message with a 500, `POST /api/sessions` returns a 5xx response carrying the session id (currently 201 because of fire-and-forget).
- [x] 1.10 Write failing test asserting that when the worker is unreachable past the dispatch timeout, the response is `202 Accepted` with the session id.
- [x] 1.11 Replace the `Task.Run(...)` block at `SessionsController.Create` (`SessionsController.cs:145–160`) with an awaited bounded-timeout call to `sessionService.SendMessageAsync`. Map the outcomes to `201 Created` / `202 Accepted` / 5xx per design D2.
- [x] 1.12 Existing happy-path tests in `SessionsApiTests` continue to pass.

### FI-5 ClaudeSessionStore atomic update

- [x] 1.13 Write failing concurrency stress test in `tests/Homespun.Tests/Features/ClaudeCode/ClaudeSessionStoreConcurrencyTests.cs` per design D3 (N=100 threads × 1000 ops, asserts no `InvalidOperationException` and no dropped writes).
- [x] 1.14 Write a deterministic regression test asserting that an `Update` racing a `Remove` does not re-insert the removed session.
- [x] 1.15 Replace the `ContainsKey` + indexer body of `ClaudeSessionStore.Update` with `TryGetValue` + `TryUpdate` (or `AddOrUpdate` per design). All existing `ClaudeSessionStoreTests` still pass.

### FI-3+4+5 ship gate

- [x] 1.16 Run the full Constitution IV pre-PR gate.
- [ ] 1.17 Update Fleece: `fleece edit oW5gur -s review --linked-pr <PR>`, `fleece edit CcVxJ3 -s review --linked-pr <PR>`, `fleece edit pa2peh -s review --linked-pr <PR>`.

---

## 2. Phase 2 — Plan artefact lifecycle (FI-6)

**Scope:** server + web change owning the lifetime of `PlanFilePath` files.

- [x] 2.1 Write failing test asserting `IClaudeSessionService.StopSessionAsync` deletes the plan file at `ClaudeSession.PlanFilePath` (currently leaks). _(Realised as `SessionLifecyclePlanArtefactTests.StopSessionAsync_invokes_RemoveForSessionAsync_for_the_session` against the new `IPlanArtefactStore` collaborator; deletion behaviour itself is unit-tested in `PlanArtefactStoreTests`.)_
- [x] 2.2 Write failing test asserting `DockerAgentExecutionService.RestartContainerAsync` and the container-removal cleanup path delete the plan file. _(Realised as `SessionLifecyclePlanArtefactTests.RestartSessionAsync_invokes_RemoveForSessionAsync_and_clears_session_plan_state` — the `SessionLifecycleService.RestartSessionAsync` path owns the cleanup the spec describes; container-recovery cleanup discovers but does not remove containers and therefore needs no plan-file delete.)_
- [x] 2.3 Write failing component test for `PlanApprovalPanel` covering the missing-plan-file path: when the read endpoint returns 404, the panel renders the "plan file no longer available" affordance and does not throw. _(`PlanApprovalPanel` was deleted in the assistant-ui migration; plan approval is now the `propose-plan` tool renderer which uses `args.planContent` embedded in the tool call and does not read the file. The closest live surface is `SessionPlansTab`; component test added there asserting the missing-content affordance renders when `usePlanContent` returns `undefined`.)_
- [x] 2.4 Write failing hook test for `usePlanFiles` asserting that a 404 from the read endpoint yields `null` content rather than throwing. _(Realised on `usePlanContent` — that's the hook actually fetching content. Verifies the generated client's non-throwing 404 contract surfaces as `data: undefined`.)_
- [x] 2.5 Introduce `IPlanArtefactStore` (or extend `MessageCacheStore`) with `RegisterAsync`, `RemoveForSessionAsync`, `TryReadAsync` per design D4. _(Introduced `IPlanArtefactStore` + `PlanArtefactStore`. `MessageCacheStore` is legacy. `TryReadAsync` collapsed into `IsRegistered` test hook because the existing `PlansController` already does the read with secure path validation; duplicating it would split the security perimeter.)_
- [x] 2.6 Wire `RemoveForSessionAsync` into `SessionLifecycleService.StopSessionAsync`, `DockerAgentExecutionService.RestartContainerAsync`, and the container-removal path. _(Wired into `SessionLifecycleService.StopSessionAsync` and `RestartSessionAsync`. `ClearContextAndStartNewAsync` reuses `StopSessionAsync` so it inherits cleanup. The DockerAgentExecutionService internal cleanup path operates on agent ids and is reached via the lifecycle service, not directly by clients — the upstream wiring is sufficient.)_
- [x] 2.7 Update the read endpoint and `usePlanFiles`/`PlanApprovalPanel` to handle a missing file gracefully. _(Read endpoint already returns 404 with `null` body via `PlansService.GetPlanContentAsync` — no server change needed. Hooks: `usePlanContent` now returns `null` (not `undefined`) on 404, locking the contract. UI: `SessionPlansTab` renders "Plan file no longer available." with a `data-testid="plan-file-missing-{fileName}"` when content is null.)_
- [x] 2.8 Run the full pre-PR gate. Update Fleece: `fleece edit c7FbVC -s review --linked-pr <PR>`. _(Pre-PR gate green: dotnet test (1875 unit + 233 API), npm run lint (warnings pre-existing, exit 0), npm run typecheck, npm run format:check, npm test (1884 web tests). Fleece status update happens at PR-open time.)_

---

## 3. Phase 3 — Playwright e2e coverage for sessions (FI-1)

**Scope:** six new specs under `src/Homespun.Web/e2e/sessions/`. Test-only — no production changes (other than `data-testid` additions where needed).

- [ ] 3.1 Audit which existing components in `features/sessions/` already have `data-testid` attributes; list missing test-ids for `PlanApprovalPanel`, the question-answer panel, the mode/model controls in `BottomSheet`/`ChatInput`, and session-history rows.
- [ ] 3.2 Add the missing `data-testid` attributes (production-side change minimal — attribute additions only).
- [ ] 3.3 `e2e/sessions/stream-session.spec.ts` (US1) — create session via UI, send a message, assert the streamed AG-UI envelope content surfaces in the message list.
- [ ] 3.4 `e2e/sessions/plan-approval.spec.ts` (US2) — trigger a plan via mock, assert `PlanApprovalPanel` mounts, approve, assert plan executes.
- [ ] 3.5 `e2e/sessions/question-answer.spec.ts` (US3) — trigger an `AskUserQuestion` mock event, assert the panel renders, answer, assert the answer is broadcast.
- [ ] 3.6 `e2e/sessions/resume-session.spec.ts` (US4) — list resumable sessions, click resume, assert the replayed events render.
- [ ] 3.7 `e2e/sessions/switch-mode-model.spec.ts` (US5) — toggle mode and model controls, assert the hub method is invoked and the UI reflects the new state.
- [ ] 3.8 `e2e/sessions/clear-interrupt-stop.spec.ts` (US6) — exercise clear-context, interrupt, and stop in turn; assert each broadcasts the correct envelope and the UI reflects the resulting state.
- [ ] 3.9 Run `npm run test:e2e -- e2e/sessions/` to green; run the full pre-PR gate. Update Fleece: `fleece edit P2ZkoA -s review --linked-pr <PR>`.

---

## 4. Phase 4 — Worker test coverage gap fill (FI-2)

**Scope:** test-only delta inside `tests/Homespun.Worker/`.

- [ ] 4.1 Run `npm run test:coverage` from `src/Homespun.Worker/` against `main`. Capture the per-file coverage for `services/session-manager.ts`, `services/session-discovery.ts`, `services/a2a-translator.ts`, `services/sse-writer.ts`. Record the baseline in the PR description.
- [ ] 4.2 Identify uncovered branches in each module — focus on error paths, boundary conditions, and the suppression rules in `a2a-translator.ts` (`AskUserQuestion` / `ExitPlanMode` re-expression).
- [ ] 4.3 Author targeted test cases for the gaps. Prefer table-driven tests with explicit fixture inputs.
- [ ] 4.4 Re-run `npm run test:coverage`; PR description must show the new module-wide coverage percentage and the per-file delta.
- [ ] 4.5 Run the full pre-PR gate. Update Fleece: `fleece edit PDEv8G -s review --linked-pr <PR>`.

---

## 5. Phase 5 — Automatic context management (FI-7)

**Scope:** the largest blast radius. Lands last; gated by `Project.ContextManagement.Mode = Off` for any project that hits trouble.

### Backend: model context-window data + token observer

- [ ] 5.1 Confirm the SDK `result.usage.input_tokens` definition (open question in design.md: cached vs uncached). If cached tokens are excluded, document the assumption; if included, adjust the ratio formula.
- [ ] 5.2 Extend `IModelCatalogService` and `ClaudeModelInfo` to carry `ContextWindow` (int, tokens). Populate from the Anthropic Models API live response and from the `FallbackModels` list.
- [ ] 5.3 Add `Project.ContextManagement` property (kebab-case `contextManagement` over JSON) with `Mode`, `SummariseThreshold`, `TrimFloor`. Default values per design D7. Persist via the existing `Project` repository.
- [ ] 5.4 Migrate stored projects to populate the new defaults on first load.
- [ ] 5.5 Implement `ISessionContextManager` service that observes `result` envelopes, computes the ratio, and decides `Summarise` / `Trim` / `Noop` per project config.

### Hub + AG-UI events

- [ ] 5.6 Define `SessionContextManagingEvent` and `SessionContextManagedEvent` AG-UI custom-event payloads in `Homespun.Shared`. Persist them through `A2AEventStore` so they survive replay.
- [ ] 5.7 Broadcast both events through the existing per-session envelope path. Verify the live==replay invariant on the new events with a unit test that captures live and then replays via `GET /api/sessions/{id}/events?mode=full`.

### Summarise / trim implementation

- [ ] 5.8 Implement the summarise turn: synthesise a "[CONTEXT SUMMARY]" prompt, push it through `ClearContextAndStartNew` so the new conversation starts with the summary as its first user turn.
- [ ] 5.9 Implement the trim path: drop oldest non-system, non-first-user messages from the AG-UI replay until the ratio drops below `TrimFloor`. Trim path SHALL be triggered only as fallback (summarise failed or post-summary ratio still > 0.9).
- [ ] 5.10 Unit tests for both paths covering the scenarios in the spec delta.

### Web

- [ ] 5.11 Render a banner/toast on `SessionContextManaging` ("Trimming conversation to fit the context window…").
- [ ] 5.12 Render a closing toast on `SessionContextManaged` summarising the strategy and tokens reclaimed.
- [ ] 5.13 Add a per-project setting UI in the project settings page to configure `Mode`, `SummariseThreshold`, `TrimFloor`.

### Tests + ship

- [ ] 5.14 Component test for the banner/toast components.
- [ ] 5.15 API integration test asserting the per-project config round-trips.
- [ ] 5.16 e2e test under `e2e/sessions/context-management.spec.ts` exercising `Mode = Auto` against a mock that emits a high-token-usage `result` envelope, and asserting both events surface in the UI.
- [ ] 5.17 Run the full pre-PR gate. Update Fleece: `fleece edit 1U3jzM -s review --linked-pr <PR>`.

---

## 6. Verify & close-out

- [ ] 6.1 With Phases 1–5 merged, manually exercise each user story end-to-end in `dev-mock` to confirm regression-free behaviour.
- [ ] 6.2 Update `openspec/changes/claude-agent-sessions/tasks.md` to check off T022, T023, T040, T047, T055, T056, T064, T070, T078, T079, T080, T082, T083 (the deferred items) and remove the `**DEFERRED → fleece:EYE183**` annotations.
- [ ] 6.3 Archive this change under `openspec/changes/archive/<date>-close-out-claude-agent-sessions-migration-gaps/` per the OpenSpec archive workflow.
- [ ] 6.4 `fleece edit OMfXwp -s complete` to close the verify issue.

---

## Dependencies & Execution Order

- Phases 1, 2, 3, 4 are independent — any pairing can land in either order.
- Phase 5 (FI-7 context management) lands LAST so the foundational fixes are already in production.
- Phase 6 (verify + archive) requires every prior phase merged.

## Parallel Opportunities

- Phase 3 (e2e tests) and Phase 4 (worker tests) touch disjoint codebases — parallel-safe.
- Phase 1's three sub-fixes (FI-3, FI-4, FI-5) touch disjoint files — parallel-safe within the phase.
- FI-6 (Phase 2) shares `DockerAgentExecutionService.cs` with FI-3 (Phase 1); land Phase 1 first to avoid merge conflicts.
