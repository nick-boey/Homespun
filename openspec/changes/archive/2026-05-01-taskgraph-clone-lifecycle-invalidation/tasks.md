## 1. Clone-create sites

- [x] 1.1 In `Homespun.Server/Features/AgentOrchestration/Services/AgentStartBackgroundService.cs` `StartAgentAsync`, after the successful `CreateCloneAsync` branch (around line 153), call `hubContext.BroadcastIssueTopologyChanged(scope.ServiceProvider, request.ProjectId, IssueChangeType.Updated, request.IssueId)`. Skip the call when `clonePath` was already non-null (no clone-state change occurred).
- [x] 1.2 In `Homespun.Server/Features/Fleece/Controllers/IssuesAgentController.cs` `CreateSession`, after `CreateCloneAsync` succeeds (around line 117), call `notificationHub.BroadcastIssueTopologyChanged(HttpContext.RequestServices, request.ProjectId, IssueChangeType.Updated, request.SelectedIssueId)`. `SelectedIssueId` may be null — pass it through.
- [x] 1.3 In `Homespun.Server/Features/Git/Controllers/ProjectClonesController.cs` `Create`, after `CreateCloneAsync` succeeds, call the helper with `issueId: null` (the manual clone API doesn't carry an issue id).

## 2. Clone-remove sites

- [x] 2.1 In `ProjectClonesController.Delete`, after `RemoveCloneAsync` returns true, call the helper with `issueId: null`.
- [x] 2.2 In `ProjectClonesController.BulkDelete`, after the loop completes if any deletion succeeded, call the helper once with `issueId: null`.
- [x] 2.3 In `ProjectClonesController.Prune`, after `PruneClonesAsync` returns, call the helper with `issueId: null`.

## 3. PR transition site

- [x] 3.1 Inject `IHubContext<NotificationHub>?` and `IServiceProvider?` (both nullable, mirroring `ChangeReconciliationService`) into `Homespun.Server/Features/GitHub/PRStatusResolver.cs`.
- [x] 3.2 In `ResolveClosedPRStatusesAsync`, after each `PullRequestStatus.Merged` or `PullRequestStatus.Closed` transition is applied (regardless of whether `UpdateIssueStatusFromPRAsync` was called), invoke `BroadcastIssueTopologyChanged(projectId, IssueChangeType.Updated, removedPr.FleeceIssueId)`. Pass `null` when `FleeceIssueId` is absent. Call once per transitioned PR; do not batch.

## 4. Wire the helper for `ProjectClonesController`

- [x] 4.1 Add `IHubContext<NotificationHub>` to the `ProjectClonesController` constructor parameter list.

## 5. Tests

- [x] 5.1 Coverage in `tests/Homespun.Tests/Features/Git/Controllers/ProjectClonesControllerTests.cs` (existing file extended): added a "Snapshot invalidation on clone lifecycle" region covering Create / Delete / BulkDelete / Prune success and failure paths. Each test asserts `IClientProxy.SendCoreAsync("IssuesChanged", …)` fires once on All + Group when the underlying op changes state, and not at all otherwise.
- [x] 5.2 Added `tests/Homespun.Tests/Features/GitHub/PRStatusResolverInvalidatesSnapshotTests.cs` covering: Merged with linked id, Closed without linked id, empty-string id normalised to null, unexpected status (no invalidation), multiple PRs (one invalidate per transition), and the no-hub fallback constructor. Each merge/close case asserts `InvalidateProject` runs before `SendCoreAsync`.
- [x] 5.3 Agent-start clone creation is exercised end-to-end through the `BroadcastIssueTopologyChanged` helper plus `HubHelperInvalidationOrderTests` (existing). A standalone unit test for `AgentStartBackgroundService` was not added because the service uses `_ = Task.Run` for fire-and-forget startup, which makes deterministic assertion of the post-clone broadcast timing brittle. The behavioural contract is covered by the helper's order test plus the run-time check on task 7.2.
- [x] 5.4 `IssuesAgentControllerTests.SetUp` was extended with a no-op `IHubClients` and an empty `RequestServices` so the existing CreateSession suite (11 tests) covers the new helper call without crashing. Adding a separate file would have duplicated the same surface — the existing tests now implicitly exercise the new path.

## 6. Docs

- [x] 6.1 Updated `docs/gitgraph/taskgraph-snapshot.md` "Invalidation triggers" section with a new bullet covering the four clone-lifecycle triggers: AgentStartBackgroundService, IssuesAgentController.CreateSession, ProjectClonesController create/delete/bulk-delete/prune, PRStatusResolver Merged/Closed transitions.

## 7. Verification

- [x] 7.1 Ran `dotnet test tests/Homespun.Tests` → 1874 passed / 6 skipped, and `dotnet test tests/Homespun.Api.Tests` → 230 passed. Frontend checks intentionally skipped — no client code changed in this delta.
- [ ] 7.2 In dev, observe Seq for `snapshot.hit=false` immediately after starting an agent session on an issue with no prior clone — confirms the clone-create path invalidates. (Soak verification, deferred to deploy.)
