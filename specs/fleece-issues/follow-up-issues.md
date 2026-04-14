# Follow-Up Issues — Fleece Issues (migrated)

**Status**: Created in Fleece on 2026-04-14. The `FI-N` labels below map to real Fleece IDs per the table. Use `fleece show <id> --json` for live details; this file is a historical record of the migration-time scope.

| Label | Fleece ID | Type | Title |
|---|---|---|---|
| FI-1 | `KSXXVP` | chore | Rename FleeceService/IFleeceService files to match class names |
| FI-2 | `yf2OTa` | chore | Split FleeceChangeApplicationService.cs into focused files |
| FI-3 | `JIGk7h` | chore | Rationalise the two IssueDto.cs files across Shared namespaces |
| FI-4 | `vOx09w` | chore | Delete or wire up the unused RunAgentResponse DTO |
| FI-5 | `KuhsyT` | task  | Implement Load More PRs in task graph view |
| FI-6 | `Xqg4qH` | chore | Wrap assignees endpoint response in a DTO |
| FI-7 | `SGhfxQ` | task  | Add e2e coverage for Fleece sync and undo/redo |
| FI-8 | `0HQOyi` | chore | Document or normalise IssuesAgent route addressing |
| FI-9 | `Nee2Cy` | chore | Make issue history depth configurable |

---

## FI-1 — Rename FleeceService/IFleeceService files to match class names

- **Fleece ID**: `KSXXVP`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T018 ; `plan.md` GP-1 ; `spec.md` §A-1
- **Why**: Constitution §VIII naming symmetry. `FleeceService.cs` contains `ProjectFleeceService`; `IFleeceService.cs` contains `IProjectFleeceService`. Filename/class mismatch makes navigation and grep confusing.
- **Deliverables**:
  - Rename `src/Homespun.Server/Features/Fleece/Services/FleeceService.cs` → `ProjectFleeceService.cs`.
  - Rename `src/Homespun.Server/Features/Fleece/Services/IFleeceService.cs` → `IProjectFleeceService.cs`.
  - Update any `#load` / test-fixture references (unlikely; C# doesn't require this, but confirm IDE tooling doesn't mis-reference).
  - Run `dotnet test` to confirm no references break.

---

## FI-2 — Split FleeceChangeApplicationService.cs into focused files

- **Fleece ID**: `yf2OTa`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T019 ; `plan.md` GP-2 ; `spec.md` §A-2
- **Why**: `FleeceChangeApplicationService.cs` is 881 lines and hides `IFleeceConflictDetectionService` + `FleeceConflictDetectionService` at the end of the file. Other services in the slice follow one-public-type-per-file.
- **Deliverables**:
  - Split into `IFleeceChangeApplicationService.cs`, `FleeceChangeApplicationService.cs`, `IFleeceConflictDetectionService.cs`, `FleeceConflictDetectionService.cs`.
  - Keep tests file-aligned: consider splitting `FleeceChangeApplicationServiceTests.cs` into one file per new public type if it becomes unwieldy post-split.
  - Run `dotnet test` and `npm run generate:api:fetch` (in case any of these types appear in the OpenAPI surface).

---

## FI-3 — Rationalise the two IssueDto.cs files across Shared namespaces

- **Fleece ID**: `JIGk7h`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T020 ; `plan.md` GP-3 ; `spec.md` §A-3
- **Why**: Two `IssueDto.cs` files with different type names in sibling namespaces (`Homespun.Shared.Models.Fleece.IssueDto.cs` → `IssueResponse`; `Homespun.Shared.Models.Issues.IssueDto.cs` → `IssueDto`). Grep hits the wrong one roughly half the time.
- **Options** (pick one):
  - (a) Rename `Fleece/IssueDto.cs` → `IssueResponse.cs` and `Issues/IssueDto.cs` → `AgentIssueDto.cs` to match the types.
  - (b) Consolidate into a single `IssueDto` under `Shared/Models/Issues/`; have `IssueResponse` alias it (or have the API return `IssueDto` directly and retire `IssueResponse`).
  - (c) Document the reasoning in `spec.md §A-3` and accept the status quo.
- **Deliverables**:
  - Choice recorded in `spec.md §A-3`.
  - File rename or consolidation executed if (a) or (b).
  - OpenAPI client regenerated.

---

## FI-4 — Delete or wire up the unused RunAgentResponse DTO

- **Fleece ID**: `vOx09w`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T115 ; `plan.md` GP-4 ; `spec.md` §A-4
- **Why**: `RunAgentResponse` is defined in `src/Homespun.Shared/Requests/IssueRequests.cs` but has no callers. `POST /api/issues/{id}/run` returns `RunAgentAcceptedResponse` (202). Dead code in a shared contract is a future-bug magnet.
- **Deliverables**:
  - Delete `RunAgentResponse` from `IssueRequests.cs`.
  - Run `npm run generate:api:fetch` and verify the generated client no longer emits the type.
  - Run `dotnet test` + `npm test` to confirm nothing depends on it.

---

## FI-5 — Implement Load More PRs in task graph view

- **Fleece ID**: `KuhsyT`
- **Type**: `task`
- **Referenced by**: `tasks.md` T029 ; `plan.md` GP-5 ; `spec.md` §A-8
- **Why**: `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx:1103` contains a `// TODO: Implement load more PRs`. The LoadMore row renders with no handler; clicking does nothing. Practically harmless (rarely >1 PR per issue) but a known gap.
- **Options** (pick one):
  - (a) Wire to a paging call (`GET /api/issues/{issueId}/prs?offset=N`) — requires a new endpoint.
  - (b) Remove the LoadMore row entirely and accept that the full PR list is always loaded.
- **Deliverables**:
  - Decision recorded.
  - Implementation landed with a component test covering the chosen path (click loads N more / row no longer renders).

---

## FI-6 — Wrap assignees endpoint response in a DTO

- **Fleece ID**: `Xqg4qH`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T039 ; `plan.md` GP-6 ; `spec.md` §A-5
- **Why**: `GET /api/projects/{projectId}/issues/assignees` returns `List<string>` (raw email strings); every other endpoint in the slice uses a typed response DTO. Future additions (display name, avatar URL, role) need a wrapper.
- **Deliverables**:
  - Introduce `ProjectAssigneesResponse { List<string> Assignees }` or richer `AssigneeDto` in `src/Homespun.Shared/Models/Fleece/`.
  - Update `IssuesController.GetProjectAssignees` to return the wrapper.
  - Run `npm run generate:api:fetch` and update `use-project-assignees.ts` to unwrap.

---

## FI-7 — Add e2e coverage for Fleece sync and undo/redo

- **Fleece ID**: `SGhfxQ`
- **Type**: `task` (or break into multiple sub-tasks with a `verify` parent)
- **Referenced by**: `tasks.md` T097 + T104 ; `plan.md` GP-7 ; Constitution §IV
- **Why**: Playwright e2e does not touch `/api/fleece-sync/*` or `/api/projects/{projectId}/issues/history/{state,undo,redo}`. Both surfaces are load-bearing for multi-contributor and safety workflows.
- **Suggested scope**: `src/Homespun.Web/e2e/`
  - `fleece-sync.spec.ts` — sync happy path, pull-while-behind-remote, discard-non-fleece-and-pull preserves `.fleece/` changes.
  - `fleece-history.spec.ts` — undo after create, redo after undo, no-op when history is empty.
- **Suggested create command**:
  ```bash
  fleece create -t "Add Playwright e2e for Fleece sync + history" -y task \
    -d "Add e2e specs under src/Homespun.Web/e2e/ covering sync happy path, pull-while-behind, discard-and-pull, undo-after-create, and redo-after-undo. Referenced by specs/fleece-issues/tasks.md T097/T104 and plan.md GP-7."
  ```
  (Already created as `fleece:SGhfxQ`.)

---

## FI-8 — Document or normalise IssuesAgent route addressing

- **Fleece ID**: `0HQOyi`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T116 ; `plan.md` GP-8 ; `spec.md` §A-7
- **Why**: `IssuesAgentController` uses `/api/issues-agent/{sessionId}/*`; every other Fleece controller uses `{projectId}` or `{issueId}` keys. The asymmetry is deliberate (sessions carry their own context) but undocumented.
- **Options** (pick one):
  - (a) Expand `spec.md §A-7` with the rationale and declare the pattern canonical for any future session-scoped API.
  - (b) Realign `IssuesAgentController` to include `/api/projects/{projectId}/issues-agent/{sessionId}/*`.
- **Deliverables**:
  - Decision recorded in `spec.md §A-7`.
  - If realigning: update every endpoint, update `features/issues-agent/hooks/*` to pass `projectId`, regenerate OpenAPI client, update `sessions.$sessionId.issue-diff.tsx` tests.

---

## FI-9 — Make issue history depth configurable

- **Fleece ID**: `Nee2Cy`
- **Type**: `chore`
- **Referenced by**: `tasks.md` T110 ; `plan.md` GP-9 ; `spec.md` §A-6 / FR-006
- **Why**: `IIssueHistoryService.MaxHistoryEntries = 100` is a hard-coded constant at `src/Homespun.Server/Features/Fleece/Services/IIssueHistoryService.cs:16`. High-churn projects may want larger buffers; tests may want smaller. Configuration is standard for any ring-buffered value.
- **Deliverables**:
  - Introduce `FleeceHistoryOptions { int MaxEntries = 100 }` bound via `IOptions<>` in `Program.cs`.
  - Update `IssueHistoryService` to read from the options.
  - Add an `appsettings.json` entry (commented, for discoverability).
  - Add `IssueHistoryServiceTests::MaxHistoryEntries_BoundHonoured_WhenConfigured` asserting that a custom value prunes at the configured size.

---

## Notes for the humans running `fleece edit`

- All nine issues are created in `open` status. Use `fleece edit <id> -s progress` when starting work.
- `FI-1` / `FI-2` / `FI-3` are pure refactors — candidates to bundle into a single "Fleece Issues structural cleanup" PR. File moves only; no behaviour change.
- `FI-4` and `FI-6` both require rerunning `npm run generate:api:fetch` — batch into one PR to avoid two client regens.
- `FI-7` is the biggest piece by wall time; consider converting to a `verify` parent with per-spec sub-tasks via `fleece edit <child-id> --parent-issues SGhfxQ:<lex-order>`.
- Cross-link each work-in-progress PR back into `tasks.md` with the issue ID so the task list stays truthful.
