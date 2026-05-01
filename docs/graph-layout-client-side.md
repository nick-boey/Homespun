# Client-side graph layout

This document covers the architecture introduced by the
`move-graph-layout-to-client` OpenSpec change. The frontend now owns
graph layout end-to-end — the server sends raw issue data + decoration
maps, and the client runs the same algorithm Fleece.Core ships in C#
to position nodes / edges before render.

## Module structure

```
src/Homespun.Web/src/features/issues/services/layout/
  index.ts                       public exports
  types.ts                       LayoutMode, EdgeKind, GraphLayout<T>, …
  graph-layout-service.ts        generic GraphLayoutService<TNode>
  issue-layout-service.ts        layoutForTree, layoutForNext (issue-aware)
  edge-router.ts                 occupancy grid, walkEdge routing
  graph-layout-service.test.ts   algorithmic unit tests (generic)
  issue-layout-service.test.ts   algorithmic unit tests (issue-aware)
  edge-router.test.ts            edge geometry tests
  golden-fixtures.test.ts        cross-stack diff against C# reference
  fixtures/                      mirrors tests/Homespun.Web.LayoutFixtures
```

`task-graph-layout.ts` (one level up) is the rendering wrapper. It
exposes `computeLayoutFromIssues({ issues, decorations, viewMode, … })`,
runs `layoutForTree` / `layoutForNext`, then synthesises Homespun-only
rows (PR rows, separators, "load more"). The legacy
`computeLayout(taskGraph, …)` overload remains for the static diff
view, which still consumes the server-laid-out `TaskGraphResponse`.

## Golden-fixtures workflow

The TS port duplicates an algorithm that lives in a separately-versioned
external library (`Fleece.Core`). Drift detection at upgrade time
matters more than internal-refactor protection, so we keep a corpus of
input/output pairs in `tests/Homespun.Web.LayoutFixtures/fixtures/`.

`tests/Homespun.Web.LayoutFixtures/EmitFixturesTests.cs` is the C# side
of the diff:

- **Read-only mode (default):** loads each `*.input.json`, runs the live
  `IIssueLayoutService.LayoutForTree` / `LayoutForNext`, asserts the
  output matches the existing `*.expected.json`. Drift in either
  direction (algorithm change or fixture bit-rot) fails the test.
- **Update mode (`UPDATE_FIXTURES=1`):** rewrites every `*.expected.json`
  from the live engine. Use this when:
  1. Authoring a new fixture (input is committed, expected is generated).
  2. A Fleece.Core upgrade lands and the algorithm intentionally
     changed. Diff the fixture changes; update `types.ts` and the TS
     port until `golden-fixtures.test.ts` passes; commit both.

```bash
# Author / regenerate expected files (after Fleece upgrade):
UPDATE_FIXTURES=1 dotnet test tests/Homespun.Web.LayoutFixtures \
  --filter Category=Fixtures

# Read-only verification (CI):
dotnet test tests/Homespun.Web.LayoutFixtures --filter Category=Fixtures
```

The TS side (`golden-fixtures.test.ts`) is part of the regular `npm
test` suite and asserts the port's output matches the `.expected.json`
files structurally.

## Edge rendering semantics

`task-graph-svg.tsx`'s `buildEdgePath` produces arc-cornered orthogonal
paths. Three branches by `edge.kind`:

- **`SeriesSibling`**: vertical run between siblings; if the lane is
  the same a plain `M…L` line, otherwise a quarter-arc into the target
  attach side.
- **`SeriesCornerToParent`**: vertical run from the last child up,
  quarter-arc, horizontal hop into the parent's left attach side.
- **`ParallelChildToSpine`**: horizontal hop from the child onto the
  spine lane, quarter-arc into vertical, run down the spine; when the
  target sits off the spine a second arc bends back out. Same-row hops
  emit a plain horizontal line.

The corner radius is clipped per edge to
`min(EDGE_CORNER_RADIUS, |perpendicular spans|)` so tight spacing
doesn't overrun the target attach point. The Storybook stories under
`features/issues/TaskGraphSvg/EdgeKinds` cover each kind plus a
many-edges scene and a tight-spacing scene.

## Unified `IssueChanged` event

Server: `BroadcastIssueChanged(this IHubContext<NotificationHub>,
string projectId, IssueChangeType kind, string? issueId, IssueResponse?
issue)` is the single broadcast helper. Every issue mutation path
(create / update / delete / set-parent / move-sibling / accept-changes
/ undo / redo / fleece-sync / change-reconciliation / clone lifecycle /
PR status transition) emits one `IssueChanged` event, carrying the
canonical post-mutation issue body for create and update kinds and
`null` for delete. `issueId` may be `null` for bulk events (clone /
fleece-sync flows) — clients treat a null id as "invalidate every
issue cache for this project". The legacy `BroadcastIssueTopologyChanged`
+ `BroadcastIssueFieldsPatched` pair, the snapshot store / refresher,
and the `PatchableField` / `IssueFieldPatch` machinery are deleted.

Client: `applyIssueChanged(cache, event)` performs an idempotent merge.
Replace-by-id is the contract:

- `created` / `updated` → replace the matching id (insert when absent).
- `deleted` → drop the matching id.
- Order does not matter; applying twice is a no-op. The local POST
  response and the SignalR echo can both apply the mutation without
  request-id dedup or echo-suppression.

The hooks in `features/issues/hooks/` wire `applyIssueChanged` for the
`useIssues` cache and invalidate-on-event for every decoration cache
(`useLinkedPrs`, `useAgentStatuses`, `useOpenSpecStates`,
`useOrphanChanges`, `useMergedPrs`). On `HubConnection.onreconnected`
each hook invalidates its query key so events missed during the
disconnected window are recovered.

## When to regenerate fixtures

- Bumping `Fleece.Core` to a version with non-trivial layout changes.
  The version pinned in `tests/Homespun.Web.LayoutFixtures/README.md`
  is the bookkeeping marker.
- Adding a fixture for a topology not yet covered (e.g. specific
  multi-parent diamond shape that surfaced a bug).

When in doubt, run `UPDATE_FIXTURES=1` once and review the diff before
committing — the diff is the precise statement of what the algorithm
emits for that input.
