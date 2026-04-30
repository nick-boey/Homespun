## ADDED Requirements

### Requirement: Branchless link mode discovers and writes every clone in one request

`POST /api/openspec/changes/link` SHALL accept a request body with `branch` null/empty and SHALL atomically write a `.homespun.yaml` sidecar into every tracked clone for the project that carries `openspec/changes/<changeName>/`. The clones in scope are the project's main clone (`Project.LocalPath`) plus every entry returned by `IGitCloneService.ListClonesAsync(project.LocalPath)`.

If no clone carries the change directory, the endpoint SHALL return 404. Otherwise it SHALL return 204 after every matched clone has had its sidecar written; if writing any sidecar fails, the request SHALL fail with a 5xx and SHALL NOT roll back already-written sidecars (best-effort within one server request, no transactional boundary across clones is implied).

The branch-scoped form (`branch` non-empty) SHALL continue to write a sidecar to the single named clone with its existing 404 semantics. The branchless form is an additive mode, not a replacement.

After writing, the controller SHALL invalidate the cached `BranchStateSnapshot` for every matched non-main branch, and SHALL invalidate the task-graph snapshot for the project once.

#### Scenario: Branchless link writes to every clone carrying the change directory

- **WHEN** `POST /api/openspec/changes/link` is called with `{ projectId, changeName: "X", fleeceId: "F" }` and `branch` omitted
- **AND** the project's main clone and one branch clone both carry `openspec/changes/X/`
- **THEN** the endpoint SHALL write `.homespun.yaml` with `fleeceId: F` and `createdBy: server` into both clones
- **AND** the response SHALL be 204 No Content

#### Scenario: Branchless link returns 404 when no clone carries the change

- **WHEN** `POST /api/openspec/changes/link` is called with `branch` omitted
- **AND** no tracked clone (main or branch) carries `openspec/changes/<changeName>/`
- **THEN** the endpoint SHALL return 404

#### Scenario: Branch-scoped form preserves single-clone semantics

- **WHEN** `POST /api/openspec/changes/link` is called with `branch: "feat/x"` set
- **AND** the named clone carries `openspec/changes/<changeName>/`
- **THEN** the endpoint SHALL write the sidecar to that single clone only
- **AND** the response SHALL be 204 No Content
- **AND** sidecars on other clones (e.g. main) carrying the same directory SHALL NOT be touched

#### Scenario: Branchless link invalidates all matched branch caches

- **WHEN** the branchless form succeeds with N matched non-main branch clones
- **THEN** the cached `BranchStateSnapshot` for each of those N branches SHALL be invalidated
- **AND** the project task-graph snapshot SHALL be invalidated exactly once

## MODIFIED Requirements

### Requirement: Orphan link is a single branchless server call

The client SHALL treat the link action on a deduplicated orphan row as a single mutation. The hook `useLinkOrphan` SHALL emit exactly one `POST /api/openspec/changes/link` per invocation with `{ projectId, changeName, fleeceId }` and no `branch` field; the server's branchless mode discovers every clone carrying the change directory and writes every sidecar within that one request.

A server-side failure SHALL surface as a single mutation rejection carrying the server's error message; partial-success modes (some sidecars written, some not) SHALL NOT be observable to the client because the discovery and write happen in one server request, not across multiple client-driven calls.

Rationale: the prior client-side fan-out was a workaround for the endpoint's single-clone shape and intentionally accepted partial-failure as a trade-off. The server now owns clone discovery, so the trade-off is moot.

#### Scenario: Single-occurrence orphan emits one link call

- **WHEN** the user commits a link selection on an orphan with a single occurrence
- **THEN** the client SHALL emit exactly one `POST /api/openspec/changes/link` with `{ projectId, changeName, fleeceId }` and no `branch` field

#### Scenario: Multi-occurrence orphan also emits exactly one call

- **WHEN** the user commits a link selection on an orphan with two or more occurrences
- **THEN** the client SHALL still emit exactly one `POST /api/openspec/changes/link` with `{ projectId, changeName, fleeceId }`
- **AND** the server SHALL be the component that writes one sidecar per matched clone

#### Scenario: Cache invalidation runs once after the call resolves

- **WHEN** the single fan-out call resolves (succeeded or rejected)
- **THEN** the client SHALL invalidate the `['task-graph', projectId]` query exactly once

#### Scenario: Server error surfaces as a single rejection

- **WHEN** the server responds with an error
- **THEN** the mutation SHALL reject with the server's `detail` field as the error message
