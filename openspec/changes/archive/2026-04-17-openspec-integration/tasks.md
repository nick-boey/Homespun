## 1. Backend: sidecar service

- [x] 1.1 Create `Features/OpenSpec/` slice with `Services/` and `Controllers/` subdirectories
- [x] 1.2 Implement `ISidecarService` — read/write `.homespun.yaml` files in `openspec/changes/<name>/` directories
- [x] 1.3 Define sidecar model: `{fleeceId, createdBy}` serialised as YAML
- [x] 1.4 Write unit tests: read existing sidecar, write new sidecar, handle missing file, handle malformed YAML

## 2. Backend: branch scanner service

- [x] 2.1 Implement `IChangeScannerService` — enumerate `openspec/changes/*` in a clone path, read sidecars, match against branch fleece-id suffix
- [x] 2.2 Filter inherited changes (sidecar fleeceId != branch fleece-id)
- [x] 2.3 Fallback: scan `openspec/changes/archive/*` when live change not found
- [x] 2.4 Auto-transition fleece issue to `complete` when archived change detected
- [x] 2.5 Detect orphan changes (no sidecar, newly created on branch via `git log --diff-filter=A`)
- [x] 2.6 Auto-write sidecar for single-orphan case post-session
- [x] 2.7 Shell out to `openspec status --change <name> --json` to get artifact state per change
- [x] 2.8 Parse tasks.md checkboxes to determine task completion state
- [x] 2.9 Write unit tests with fixture branches: linked change, inherited change filtering, orphan detection, archived fallback, artifact state, task completion

## 3. Backend: snapshot contract

- [x] 3.1 Define snapshot DTO: `{branch, fleeceId, changes[{name, artifactState, tasksDone, tasksTotal, nextIncomplete}]}`
- [x] 3.2 Create `ChangeSnapshotController` with `POST /api/openspec/branch-state` endpoint
- [x] 3.3 Implement in-memory cache keyed by `(projectId, branch)` with 60s TTL
- [x] 3.4 Write integration test: POST snapshot → GET graph includes change state

## 4. Backend: on-demand scan

- [x] 4.1 Add scan-on-demand logic to graph endpoint: if no cached snapshot or TTL expired, scan on-disk clone
- [x] 4.2 Cache the scan result with same TTL
- [x] 4.3 Write integration test: cold cache triggers live scan

## 5. Backend: extend issue graph with change state

- [x] 5.1 Extend `IssueGraphService` (or equivalent) to merge change state into graph response DTOs
- [x] 5.2 Add fields to issue graph DTO: `branchState` (none/exists/withChange), `changeState` (none/incomplete/readyToApply/readyToArchive/archived), `changeName`, `phases[{name, done, total}]`
- [x] 5.3 Add orphan changes to graph response (branch-scoped under issue, main-scoped as separate section)
- [x] 5.4 Write unit tests for graph merging: linked change, orphan rendering, archived state

## 6. Worker: post-session snapshot hook

- [x] 6.1 Add post-session hook in worker that scans `openspec/changes/` on the branch clone
- [x] 6.2 Compose snapshot JSON from scan results
- [x] 6.3 POST snapshot to server at `POST /api/openspec/branch-state`
- [x] 6.4 Write unit test: session end triggers scan and POST

## 7. Frontend: OpenSpec tab in run-agent panel

- [x] 7.1 Replace the Workflow tab with an OpenSpec tab (depends on `remove-workflows`)
- [x] 7.2 List all 8 OpenSpec skills with name and description
- [x] 7.3 Implement auto-selection logic: explore (default) → apply (all artifacts) → archive (all tasks)
- [x] 7.4 Implement readiness gating: `apply`, `verify`, `sync`, `archive` blocked when prerequisites not met; `explore`, `propose`, `new-change`, `continue-change` always available
- [x] 7.5 Dispatch: read selected skill's SKILL.md body + append change name → send as initialMessage
- [x] 7.6 Schema override: inject system prompt phrase when non-default schema active
- [x] 7.7 Write component tests: auto-selection for each state, readiness gating, dispatch composition

## 8. Frontend: issue graph indicators

- [x] 8.1 Add branch indicator symbol to issue rows: gray (no branch), white (branch, no change), amber (branch with change)
- [x] 8.2 Add change status symbol: none, red ◐ (incomplete), amber ◐ (ready-to-apply), green ● (ready-to-archive), blue ✓ (archived)
- [x] 8.3 Update issue node shape: round (no change) vs square (has change)
- [x] 8.4 Write component tests for each indicator state

## 9. Frontend: virtual sub-issues from tasks.md

- [x] 9.1 Parse tasks.md phases (## headings) and task counts (checkboxes) from graph response
- [x] 9.2 Render phase-level roll-up nodes under change-linked issues
- [x] 9.3 Implement phase detail modal showing leaf tasks with checkbox state on badge click
- [x] 9.4 Write component tests for phase rendering and modal

## 10. Frontend: orphan change handling

- [x] 10.1 Render orphan changes on agent branches as virtual children under the branch's issue
- [x] 10.2 Render orphan changes on main at the bottom of the graph as "Orphaned Changes" section
- [x] 10.3 Implement [link-to-issue], [create-sub-issue], and [create-issue] actions
- [x] 10.4 Write component tests for orphan rendering and action flows

## 11. Verification

- [x] 11.1 Run full backend test suite: `dotnet test` (2028+206+5 passed)
- [x] 11.2 Run frontend checks: `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test` (1976 tests passed; pre-existing lint error in run-agent-dialog.tsx:247 unrelated to this change)
- [x] 11.3 Run e2e tests: `npm run test:e2e` (68 passed; 8 pre-existing failures in prompt specs — global-prompts, none-prompt-flow, project-prompt-override, agent-and-issue-agent-launching, agui-sessions — unrelated to openspec-integration, caused by prompt catalogue → filesystem skills migration)
- [x] 11.4 Start mock server and verify: issue graph shows branch/change indicators, OpenSpec tab auto-selects correctly, orphan changes surface (verified via Playwright MCP with route interception)
- [x] 11.5 Verify archived change shows blue ✓ (ISSUE-002: changeState=archived, glyph=✓, squareNode=true)
- [x] 11.6 Verify multi-change per branch renders correctly (ISSUE-003: readyToArchive renders primary change state)
