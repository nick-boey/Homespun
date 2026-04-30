## Why

`POST /api/openspec/changes/link` writes a `.homespun.yaml` sidecar into one specific clone. The clone is named by the `branch` field on the request body: null/empty resolves to the main clone, a non-empty value resolves to the matching `.clones/<branch>` directory, and a request that names a branch which does not carry the change directory returns 404.

That single-clone shape leaks into the client. `useLinkOrphan` accepts an `occurrences: { branch, changeName }[]` collection and emits one `POST` per occurrence under `Promise.all` — one for main, one for each branch clone that carries the same change name. The fan-out is the workaround that the orphan-changes-link-picker change (`openspec/changes/archive/2026-04-21-orphan-changes-link-picker/`) shipped explicitly under D3, with the partial-failure mode (some sidecars written, others not) called out as an accepted trade-off.

A branchless server form removes the workaround. The server already owns the abstractions to enumerate every tracked clone for a project (`IGitCloneService.ListClonesAsync`) and to test for change-directory presence (existing controller logic). Lifting the fan-out from the client to the server collapses the mutation back to a single call and eliminates the partial-failure mode by writing every sidecar within one request.

## What Changes

- **Server: branchless link mode.** `ChangeSnapshotController.Link` accepts a request with `Branch` null/empty and SHALL discover every tracked clone (main clone + every entry in `IGitCloneService.ListClonesAsync`) carrying `openspec/changes/<changeName>/`, then SHALL write the sidecar to each. Returns 404 only when no clone carries the directory; returns 204 on success. Cache invalidation runs once per matched non-main branch.
- **Server: branch-scoped form preserved.** Requests with `Branch` set keep their existing single-clone semantics (resolve clone, write sidecar, invalidate the named branch). The branchless form is additive — no caller is forced to migrate.
- **Client: collapse the fan-out.** `useLinkOrphan` accepts `{ projectId, changeName, fleeceId }` and emits one `POST /api/openspec/changes/link` with no `branch` field. The `occurrences[]` shape is removed; callers pass the change name once.
- **Client: `OrphanedChangesList` caller.** Updated to pass `changeName: entry.name` instead of `occurrences: entry.occurrences`. The `OrphanEntry.occurrences` collection itself stays — the bottom-section UI still uses it for the "on N branches" label and tooltip.

## Capabilities

### Modified Capabilities

- `openspec-integration`: replaces the requirement "Orphan link fans out across all occurrences" (client-side fan-out, partial-failure trade-off) with "Orphan link is a single branchless server call" (server-side discovery, all-or-nothing write within one request). Adds a server-side requirement covering the branchless link semantics.

## Impact

**Server:**
- `Homespun.Server/Features/OpenSpec/Controllers/ChangeSnapshotController.cs` — add a branchless code path in `LinkOrphan` that enumerates clones and writes the sidecar to every clone carrying the change directory. ~40 LOC added; existing branch-scoped path untouched.

**Client:**
- `Homespun.Web/src/features/issues/hooks/use-link-orphan.ts` — remove `LinkOrphanOccurrence`, simplify `LinkOrphanParams` to `{ projectId, changeName, fleeceId }`, drop the `Promise.all` fan-out. ~30 LOC removed.
- `Homespun.Web/src/features/issues/hooks/use-link-orphan.test.ts` — rebase tests on the new shape (single call, no per-occurrence semantics).
- `Homespun.Web/src/features/issues/components/orphan-changes.tsx` — update `handleLinkSelect` and `handleCreateIssue` to pass `changeName: entry.name`.

**Tests:**
- `tests/Homespun.Api.Tests/Features/OpenSpec/ChangeSnapshotApiTests.cs` — add coverage for the branchless link form: writes to multiple clones; returns 404 when no clone has the change.

**Wire format / API client:**
- `LinkOrphanRequest.Branch` is already optional (`string?`). No DTO change needed; the OpenAPI client regen is a no-op for this contract.

**Backwards compatibility:**
- The branch-scoped form keeps working. No deprecation required for this change.
