## 1. Server: branchless link mode

- [x] 1.1 In `src/Homespun.Server/Features/OpenSpec/Controllers/ChangeSnapshotController.cs`, change `LinkOrphan` so that when `request.Branch` is null/empty it enumerates every tracked clone (main `project.LocalPath` + `IGitCloneService.ListClonesAsync(project.LocalPath)`) and collects those whose `openspec/changes/<changeName>/` directory exists. If no clone matches, return 404. Otherwise, write the sidecar to every match in one pass via `await Task.WhenAll(...)`.
- [x] 1.2 In the branchless path, invalidate `BranchStateCacheService` for every matched non-main clone (use `CloneInfo.ExpectedBranch` ?? `CloneInfo.Branch` stripped of `refs/heads/` to identify the branch key). Always call `snapshotStore?.InvalidateProject(projectId)` once at the end.
- [x] 1.3 Preserve the existing branch-scoped path (when `request.Branch` is non-empty) byte-for-byte. The new code SHALL be an additional branch on the controller, not a replacement.
- [x] 1.4 Update the controller's XML doc comment to describe both modes (branchless = scan all clones, branch-scoped = single clone).
- [x] 1.5 Bump the success log line to report the number of clones written when branchless: `"Linked orphan change {Change} to fleece issue {Fleece} across {N} clone(s)"`.

## 2. Server: tests

- [x] 2.1 Add an integration test in `tests/Homespun.Api.Tests/Features/OpenSpec/ChangeSnapshotApiTests.cs` that seeds a project with a main clone + one branch clone, both carrying `openspec/changes/test-change/`. POST `/api/openspec/changes/link` with `branch: null` and assert that both `.homespun.yaml` files are written.
- [x] 2.2 Add a test that POSTs the branchless form with a change name no clone carries; assert 404.
- [x] 2.3 Add a test that POSTs the branch-scoped form (existing semantics) and asserts a single sidecar is written to that clone only — protects backwards compat.
- [x] 2.4 Run `dotnet test tests/Homespun.Api.Tests` and confirm green.

## 3. Client: simplify hook

- [x] 3.1 In `src/Homespun.Web/src/features/issues/hooks/use-link-orphan.ts`, remove the `LinkOrphanOccurrence` export and change `LinkOrphanParams` to `{ projectId: string; changeName: string; fleeceId: string }`.
- [x] 3.2 Replace the `Promise.all` fan-out with a single `await ChangeSnapshot.postApiOpenspecChangesLink({ body: { projectId, changeName, fleeceId } })` call. Keep the existing `response.error` extraction shape so error messages still surface to the caller.
- [x] 3.3 Update the JSDoc to describe the new "single branchless call" semantics; drop the partial-failure language.

## 4. Client: tests

- [x] 4.1 Rewrite `src/Homespun.Web/src/features/issues/hooks/use-link-orphan.test.ts` to assert: (a) one POST per `mutateAsync` regardless of how many clones carry the change, (b) the body contains `{ projectId, changeName, fleeceId }` with no `branch` field, (c) onSuccess invalidates the task-graph query exactly once, (d) a server error rejects the mutation with the server's `detail` message.
- [x] 4.2 Run `npm test -- use-link-orphan` from `src/Homespun.Web` and confirm green.

## 5. Client: caller

- [x] 5.1 In `src/Homespun.Web/src/features/issues/components/orphan-changes.tsx`, update `handleLinkSelect` and `handleCreateIssue` to call `link.mutateAsync({ projectId, changeName: entry.name, fleeceId: <id> })`.
- [x] 5.2 Leave `OrphanEntry.occurrences` and the `OccurrenceLabel` component untouched — they still drive the "on N branches" label and tooltip in the UI.

## 6. Pre-PR checklist

- [x] 6.1 `dotnet test` — green
- [x] 6.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test` — green
- [x] 6.3 `npm run generate:api:fetch` — confirm the OpenAPI client diff is empty (the wire shape is unchanged)
- [x] 6.4 `openspec validate branchless-openspec-change-link --strict` — green
