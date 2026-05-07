## Why

When the user creates a new issue inline in the task graph (`o` / `shift+o`), today's UI grafts an `<InlineIssueEditor>` row into the DOM at a fixed `insertAtIndex` — the layout engine never sees the pending issue, so the user can't tell where the issue will actually land or what hierarchy they've picked. Tab / Shift+Tab toggle internal `pendingChildId` ↔ `pendingParentId` flags but produce no visual response. With graph layout now fully on the client (PR #812 / `Requirement: Client-side graph layout via TypeScript port of Fleece.Core`), we can push a virtual layout participant through the same engine and let the result drive rendering — so the user sees, before pressing Enter, exactly where the new issue will sit, with proper edges, lanes, and hierarchy.

## What Changes

- Add a new node kind `pending-issue` to the layout discriminated union (`src/Homespun.Web/src/features/issues/services/layout/nodes.ts`). The generic engine (`GraphLayoutService`) is parameterised over `IGraphNode` and stays untouched; only the discriminated-union and renderer dispatch arms grow.
- Inject the synthetic pending node into the input of `IssueLayoutService` so it gets a real row, lane, and incoming/outgoing edges. Renderer mounts `<InlineIssueEditor>` at the engine-assigned position.
- Replace the current Tab/Shift+Tab flag-flip with a 3-state-per-o-press machine that mutates the synthetic's `parentIssues` and re-runs the layout on every transition. The mapping is fully symmetric across modes (no visual jumps):
  - **Tree mode**: `o` → sibling-below S; `o`+Tab → child of S; `shift+o` → sibling-above S; `shift+o`+Shift+Tab → parent of S (replaces S's old parent — synthetic inherits S's old slot).
  - **Next mode** (mirrored due to inverted layout direction): `o` → sibling-below S; `o`+Tab → parent of S (replaces S's old parent); `shift+o` → sibling-above S; `shift+o`+Shift+Tab → child of S.
  - Cancel-back: the opposite key in the active sequence reverts the synthetic to default-sibling. All other key combinations are no-ops.
- Client-side fabricate a `sortOrder` for the synthetic's parent edge, using a midpoint-string algorithm aligned with `Fleece.Core.Sorting`'s ordinal-string sort key, padded with arbitrary suffix characters as needed. The wire-level POST contract stays unchanged — `parentIssueId` + `siblingIssueId` + `insertBefore` are still derived from the synthetic's state and the server still picks its own canonical sortOrder. The fabricated value is purely a client-preview concern.
- Filter / search / next-mode-actionable checks always pass the synthetic through. Selection, scroll-into-view, and edge rendering continue to work for the synthetic without special cases beyond the kind dispatch.
- Global keyboard shortcuts (`use-toolbar-shortcuts`) suppress `o` / `shift+o` while the synthetic editor has focus, so those keys type into the title input. Single pending state — no nested or simultaneous synthetics.
- **BREAKING (UI-only)**: the `pendingChildId` / `pendingParentId` fields on the `PendingNewIssue` type are removed; the legacy `renderInlineEditor()` graft path is deleted. The new `PendingNewIssue` shape is `{ mode, referenceIssueId, title, viewMode }`.
- Storybook coverage for every state-machine state across both modes plus happy-path (Enter), cancel (Escape), and cancel-back-to-sibling transitions.

## Capabilities

### New Capabilities
<!-- None — pending-issue rendering extends the existing client-side layout capability. -->

### Modified Capabilities
- `fleece-issue-tracking`: add a new requirement covering the pending-issue layout participant (node kind, state machine, sortOrder fabrication, filter bypass) under the existing client-side-layout area. The existing `Client-side graph layout via TypeScript port of Fleece.Core` requirement stays unchanged — this is additive.

## Impact

- **Modified code**:
  - `src/Homespun.Web/src/features/issues/services/layout/nodes.ts` — add `PendingIssueLayoutNode` + `isPendingIssueNode` guard; extend `LayoutNode` union.
  - `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts` — add `TaskGraphPendingIssueRenderLine` type and `isPendingIssueRenderLine` guard; `computeLayoutFromIssues` accepts a new optional `pendingIssue` field on its input and threads the synthetic through to the engine; emit the new render-line variant from the post-engine loop.
  - `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx` — replace `pendingChildId` / `pendingParentId` flag handlers with the state machine; thread `pendingNewIssue` into the `useMemo` dep tuple at the existing `computeLayoutFromIssues` call site; replace `renderInlineEditor()` graft with a render-switch arm; handle filter/search bypass, scroll-into-view inclusion, and selection bypass for synthetic rows; suppress `o`/`shift+o` shortcuts while editor has focus.
  - `src/Homespun.Web/src/features/issues/components/inline-issue-editor.tsx` — wire Tab/Shift+Tab callbacks to the new state-machine API; lose any one-shot flag guards.
  - `src/Homespun.Web/src/features/issues/types.ts` — `PendingNewIssue` shape replaced; old fields removed.
  - `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.ts` — bail when synthetic editor has focus.
  - New file: `src/Homespun.Web/src/features/issues/services/sort-order-midpoint.ts` — client-side ordinal-string midpoint with C#-aligned alphabet.
- **New tests**:
  - Vitest unit tests for `computeLayoutFromIssues` synthetic injection (4 hierarchy outcomes × 2 modes + default sibling + filter/search bypass + empty-matched-set next-mode).
  - Vitest unit tests for the sort-order midpoint algorithm, parity-checked against C# reference outputs in a small sample.
  - Storybook stories per state-machine state + happy-path + cancel (interaction-tested via `@storybook/test`).
  - New e2e test: Tab promotion + cancel-back-to-sibling + Enter commit + Escape cancel, both modes.
- **Out of scope**: server changes (POST contract stays); multi-pending support; Tab beyond one level (no progressive outliner nesting); phase rows participating in pre-layout (phases stay post-pass per `phase-graph-rows`).
- **Cross-stack golden fixtures** (`tests/Homespun.Web.LayoutFixtures/`) are unaffected — synthetic injection is TS-only and the C# reference layout never sees a `pending-issue` node.
- **Branch / fleece**: branch `task/show-new-issues+IWkjs3`; fleece issue `IWkjs3` to be linked via `--tags openspec=pending-issue-graph-node` once this proposal is committed.
