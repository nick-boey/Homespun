## 1. Type system: new node kind

- [x] 1.1 Add `PendingIssueLayoutNode { kind: 'pending-issue', id, childSequencing: 'series', pendingTitle, parentIssues? }` to `src/Homespun.Web/src/features/issues/services/layout/nodes.ts`; extend `LayoutNode` discriminated union; export `isPendingIssueNode` type guard
- [x] 1.2 Add `TaskGraphPendingIssueRenderLine` type to `src/Homespun.Web/src/features/issues/services/task-graph-layout.ts`; export `isPendingIssueRenderLine` type guard
- [x] 1.3 Update `PendingNewIssue` shape in `src/Homespun.Web/src/features/issues/types.ts` to `{ mode: 'sibling-below' | 'sibling-above' | 'child-of' | 'parent-of'; referenceIssueId; title; viewMode }`; remove `pendingChildId` / `pendingParentId` / `inheritedParentIssueId` / `siblingIssueId` / `insertBefore` / `insertAtIndex` / `isAbove` (the synthetic re-derives every layout-affecting field on each transition)
- [x] 1.4 TypeScript audit: every `if (isIssueNode(node))` / `if (isIssueRenderLine(line))` site builds clean against the union extension; flag callers that need explicit pending-handling vs. those that can keep skipping pending

## 2. SortOrder midpoint utility (with C# parity)

- [x] 2.1 Create `src/Homespun.Web/src/features/issues/services/sort-order-midpoint.ts` exposing `midpoint(prev: string, next: string): string` with the alphabet matching `Fleece.Core`'s sort-order writer; document the boundary cases (`prev=""`, `next=""`, `prev===next` fail-fast)
- [x] 2.2 Vitest unit tests for midpoint: ordering invariant, length-extension when codepoints adjacent, empty-prev / empty-next edge cases, equal-input fail-fast
- [x] 2.3 Add a parity fixture comparing 30+ sample neighbour pairs against C#-emitted reference outputs; commit fixture as static JSON; Vitest test loads + diffs structurally
- [x] 2.4 Generate the C# reference outputs once via a small one-off script under `tests/Homespun.Web.LayoutFixtures/` (or equivalent) that calls the `Fleece.Core` writer over the same pairs; document in `design.md` how to regenerate if the writer ever changes

## 3. Layout pipeline injection

- [x] 3.1 `computeLayoutFromIssues` in `task-graph-layout.ts` accepts a new optional `pendingIssue?: PendingIssueInput` field on its input (typed against the new state shape from 1.3)
- [x] 3.2 When `pendingIssue` is present, the driver builds a *copy* of the relevant `LayoutIssue`(s) — never mutating the cached `IssueResponse` — synthesises a `LayoutIssue`-shaped record for the synthetic, applies the reference-issue patch for `parent-of` cases (S's primary parent edge points at the synthetic; synthetic inherits S's old slot), and feeds the patched array to `IssueLayoutService.layoutForTree` / `layoutForNext`
- [x] 3.3 Post-engine emit: when the engine returns a positioned synthetic node, `task-graph-layout.ts` produces a `TaskGraphPendingIssueRenderLine` carrying the engine-assigned row/lane plus the synthetic's title and edit state
- [x] 3.4 Vitest: `computeLayoutFromIssues` synthetic injection covers each of the 4 hierarchy outcomes per mode (8 cases) + default sibling-below + default sibling-above; assert lane / row / parent edge in each
- [x] 3.5 Vitest: synthetic always passes the post-layout filter pass; synthetic always passes the next-mode `marker !== Actionable` predicate
- [x] 3.6 Vitest: next-mode with empty `matchedIds` seed plus a synthetic — synthetic still renders via the seed-fallback path
- [x] 3.7 Vitest: synthetic in a graph with a phase row reference is unchanged (phase-row hierarchy guards short-circuit before the synthetic flow runs)
- [x] 3.8 Vitest: parent-of reparenting case does not introduce a cycle (`runEngine` returns `{ ok: true, … }` and the synthetic is positioned correctly)

## 4. State machine + key handling

- [x] 4.1 In `src/Homespun.Web/src/features/issues/components/task-graph-view.tsx`, replace `handleIndent` / `handleUnindent` with a state-machine reducer keyed off `(viewMode, currentMode, key)`; reject all unmapped key combinations as no-ops
- [x] 4.2 Update `handleCreateBelow` / `handleCreateAbove` / `handleCreateAtTop` / `handleCreateAtBottom` to set the new `mode` field (`sibling-below` / `sibling-above`) instead of computing `inheritedParentIssueId` + `siblingIssueId` + `insertAtIndex` upfront — the layout call now derives those
- [x] 4.3 Add `pendingNewIssue.mode` and `pendingNewIssue.referenceIssueId` (but NOT `title`) to the `useMemo` dep tuple at the existing `computeLayoutFromIssues` call site so layout reruns on transitions but not on every keystroke
- [x] 4.4 Replace the `renderInlineEditor()` graft path with a render-switch arm in the row dispatcher that mounts `<InlineIssueEditor>` at the engine-assigned row/lane (use `isPendingIssueRenderLine` from 1.2)
- [x] 4.5 `<InlineIssueEditor>` (`src/Homespun.Web/src/features/issues/components/inline-issue-editor.tsx`) wires Tab / Shift+Tab to dispatch state-machine transitions; remove the existing one-shot guards
- [x] 4.6 `handleSave` derives `parentIssueId` + `siblingIssueId` + `insertBefore` from the synthetic's current state (via the same code path used by 3.2 for layout), POSTs via `useCreateIssue` unchanged
- [x] 4.7 Delete the now-dead `renderInlineEditor()` function and any lingering references to `pendingChildId` / `pendingParentId` / `insertAtIndex` / `isAbove` / `inheritedParentIssueId` / `siblingIssueId` / `insertBefore` on `PendingNewIssue`
- [x] 4.8 `handleRowClick` / `handleCancelEdit` continue to clear `pendingNewIssue`; verify those paths against the new shape

## 5. Filter / search / focus / shortcut bypass

- [x] 5.1 Filter pass at `task-graph-view.tsx:216-236`: short-circuit any `isPendingIssueRenderLine(line)` to keep
- [x] 5.2 Search match-count pass at `task-graph-view.tsx:260-266`: ignore the synthetic for match counting
- [x] 5.3 `selectedIndex` / arrow-key navigation: skip the synthetic (it owns its own focus); scroll-into-view continues to operate on real selection plus the synthetic when present
- [x] 5.4 `src/Homespun.Web/src/features/issues/hooks/use-toolbar-shortcuts.ts`: bail when `document.activeElement` is inside the synthetic's `<InlineIssueEditor>` root (use a stable data attribute on the editor container)
- [x] 5.5 Synthetic editor's local `onKeyDown` owns Tab / Shift+Tab (state-machine), Enter (commit), Escape (cancel)
- [x] 5.6 Vitest + RTL: typing `o` in the focused editor inserts the character; pressing arrow Left/Right moves the input cursor without changing graph selection

## 6. Storybook coverage

- [x] 6.1 Add stories under the existing `task-graph-view` Storybook file (or create a co-located `*.stories.tsx` if one doesn't exist) that mount `TaskGraphView` with seeded fixtures and a pre-set `pendingNewIssue` state via prop / parameter wiring
- [x] 6.2 Per-mode default-state stories (4): tree-sibling-below, tree-sibling-above, next-sibling-below, next-sibling-above
- [x] 6.3 Per-mode promoted-state stories (4): tree-child-of, tree-parent-of, next-parent-of, next-child-of
- [x] 6.4 Happy-path interactive story (`@storybook/test`): start in tree mode at `sibling-below`, type "New issue", press Enter → assert synthetic vanishes and a real row appears
- [x] 6.5 Cancel-via-Escape interactive story
- [x] 6.6 Cancel-back-to-sibling interactive stories: `o + Tab + Shift+Tab` returns to `sibling-below`; `Shift+O + Shift+Tab + Tab` returns to `sibling-above`
- [x] 6.7 `npm run build-storybook` passes locally and in CI

## 7. End-to-end coverage

- [x] 7.1 New Playwright e2e test under `src/Homespun.Web/e2e/`: tree-mode flow — select an issue, press `o`, type a title, press Tab, press Enter; assert the new issue is rendered as a child of the original selection in the live graph
- [x] 7.2 Same flow in next mode (asserting the parent-of reparenting outcome)
- [x] 7.3 Cancel-via-Escape e2e test
- [x] 7.4 Cancel-back-to-sibling e2e test (`o + Tab + Shift+Tab + Enter` produces a sibling, not a child)

## 8. Cleanup + verification

- [x] 8.1 Delete `inheritedParentIssueId` / `siblingIssueId` / `insertBefore` / `insertAtIndex` / `isAbove` from `PendingNewIssue` if not removed in 1.3 — ensure type checker reports no residual references
- [x] 8.2 Confirm `tests/Homespun.Web.LayoutFixtures/` golden fixtures still pass unchanged (synthetic is TS-only and does not appear in C# reference layouts)
- [x] 8.3 Pre-PR checklist: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run generate:api:fetch` (no-op expected — no API changes), `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run build-storybook` all green
- [x] 8.4 Update fleece issue `IWkjs3` with `openspec=pending-issue-graph-node` tag (`fleece edit IWkjs3 --tags "openspec=pending-issue-graph-node"`); commit `.fleece/` change with the implementation PR
- [x] 8.5 Update `docs/traces/dictionary.md` if any new spans are emitted (likely none — this is UI-only)
- [x] 8.6 Create the PR; before opening, run `fleece edit IWkjs3 -s review --linked-pr <PR#>`
