## Why

The companion change `taskgraph-mutation-invalidation` (archived 2026-04-22) closed the gap between Fleece-issue mutations and the in-memory task-graph snapshot. A second gap remains: clone lifecycle events also affect graph output but are not Fleece-mutation paths, so the snapshot lags until either the 10-second refresher tick or an unrelated mutation fires.

`TaskGraphResponse.OpenSpecStates[issueId]` is only populated when a working clone exists for the issue's branch — the enricher reads OpenSpec artefacts from the clone on disk. Three classes of event change clone presence today without invalidating the snapshot:

1. **Agent session start** creates a worktree at the resolved branch — a new `OpenSpecStates` entry may appear for the issue.
2. **Manual clone create / delete / prune** on `ProjectClonesController` — entries appear or disappear wholesale.
3. **PR transitions to Merged or Closed** observed by `PRStatusResolver` — the linked Fleece issue's status flips (Complete / Closed) and the associated clone may be garbage-collected, both of which reshape the graph for that issue.

User-perceived latency: up to 10s (refresher tick) or indefinite (until another mutation invalidates) before the `OpenSpecStates` entry appears or disappears.

## What Changes

Single delta, mirroring the helper-driven invalidation pattern from the companion change:

- **Clone-create sites:** `AgentStartBackgroundService.StartAgentAsync`, `IssuesAgentController.CreateSession`, `ProjectClonesController.Create`. After a successful `CreateCloneAsync`, call `BroadcastIssueTopologyChanged(projectId, IssueChangeType.Updated, issueId)` (issueId omitted on the manual route since the clone API doesn't carry one).
- **Clone-remove sites:** `ProjectClonesController.Delete`, `BulkDelete`, `Prune`. After a successful removal, call the helper with `issueId: null` (bulk change).
- **PR transition site:** `PRStatusResolver.ResolveClosedPRStatusesAsync`. After a Merged or Closed transition propagates to the linked Fleece issue (or to the graph cache when no link exists), call the helper with the linked `FleeceIssueId` if present, otherwise `null`.

The hub helper itself does not change. All call sites use the existing `BroadcastIssueTopologyChanged` extension method introduced in the companion change.

## Capabilities

### New Capabilities

None. This change extends the existing `openspec-integration` capability with additional triggers for the existing snapshot-invalidation requirement.

### Modified Capabilities

- `openspec-integration`: extends "Fleece mutations invalidate the task-graph snapshot before broadcasting" with three additional clone-lifecycle triggers (clone create, clone remove, PR merge/close transition).

## Impact

**Server code:**

- `Homespun.Server.Features.AgentOrchestration.Services.AgentStartBackgroundService` — invalidate after `CreateCloneAsync` succeeds (uses the existing scope's `IHubContext<NotificationHub>` + `IServiceProvider`).
- `Homespun.Server.Features.Fleece.Controllers.IssuesAgentController` — invalidate after `CreateCloneAsync` succeeds (already injects `IHubContext<NotificationHub>`).
- `Homespun.Server.Features.Git.Controllers.ProjectClonesController` — inject `IHubContext<NotificationHub>`; invalidate after `CreateCloneAsync` / `RemoveCloneAsync` / `PruneClonesAsync`.
- `Homespun.Server.Features.GitHub.PRStatusResolver` — inject `IHubContext<NotificationHub>` + `IServiceProvider`; invalidate after each Merged or Closed transition is applied.

**No new public types, no new options, no new events.** `BroadcastIssueTopologyChanged` is reused as-is.

**Tests:**

- `tests/Homespun.Tests/Features/Git/Controllers/ProjectClonesControllerInvalidatesSnapshotTests.cs` — assert the three clone endpoints invalidate via the helper.
- `tests/Homespun.Tests/Features/GitHub/PRStatusResolverInvalidatesSnapshotTests.cs` — assert PR Merged / Closed transitions invalidate.
- `tests/Homespun.Tests/Features/AgentOrchestration/AgentStartInvalidatesSnapshotTests.cs` — assert agent-start clone creation invalidates.
- `tests/Homespun.Tests/Features/Fleece/Controllers/IssuesAgentCreateSessionInvalidatesSnapshotTests.cs` — assert Issues-Agent clone creation invalidates.

**Docs:** `docs/gitgraph/taskgraph-snapshot.md` "Invalidation triggers" section gains a row for clone-lifecycle events.

**No breaking API changes.** The hub helper, `IssuesChanged` event, and all controller routes preserve their signatures.
