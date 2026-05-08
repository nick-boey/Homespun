## Context

Inline issue creation in the task graph today (`task-graph-view.tsx:154`) holds a `pendingNewIssue: PendingNewIssue | null` in component state and renders `<InlineIssueEditor>` by splicing it into the row list at `pendingNewIssue.insertAtIndex` via the bespoke `renderInlineEditor()` path. The layout engine — `IssueLayoutService` + `GraphLayoutService` under `src/Homespun.Web/src/features/issues/services/layout/` — never sees the pending issue, so the row's lane and surrounding edges reflect the world *without* it. Tab / Shift+Tab toggle internal `pendingChildId` ↔ `pendingParentId` flags on the pending state, but the handler is gated on "only allow if not already indented/unindented" because there is no real model to drive a re-flow.

After PR #812 ("move graph layout to the client"), the `IssueLayoutService` runs entirely in the browser and is parameterised over `IGraphNode` via a discriminated-union `LayoutNode`. The discriminated union currently has one kind (`'issue'`); the file comment in `nodes.ts` records the intent to layer additional kinds later. Phase rows (per `phase-graph-rows`) chose the post-pass approach because their position is a deterministic splice; pending-issue position depends on edge topology that the lane assigner has to solve, so they need to enter the pipeline *before* `runEngine`.

## Goals / Non-Goals

**Goals:**
- Push the in-progress new issue through the same client-side layout engine that every other issue goes through. The layout result drives rendering; the pending issue gets a real row, lane, and incoming/outgoing edges.
- Tab / Shift+Tab become real model edits on the synthetic's `parentIssues` and trigger a re-layout, so the user sees the resulting hierarchy live before pressing Enter.
- Symmetric, mode-aware key semantics with no visual jumps when the user promotes the new node to a parent / child relationship.
- Storybook coverage that lets a developer drive each state-machine transition locally without firing up the full app.

**Non-Goals:**
- Server-side changes. The POST contract (`parentIssueId` + `siblingIssueId` + `insertBefore`) and SignalR `IssueChanged` echo path stay as-is.
- Multi-pending support. There is at most one synthetic at a time.
- Outliner-style progressive nesting. Tab beyond the one-level promotion is a no-op.
- Phase rows participating in pre-layout. Phases stay post-pass per `phase-graph-rows`.
- Cross-stack layout-fixture parity coverage of the synthetic. Synthetic injection is TS-only; the C# reference layout never sees a `pending-issue` node.

## Decisions

### Decision 1 — New node kind (option (b)) over sentinel-id-on-issue-node

The synthetic is added as a new kind `'pending-issue'` on the `LayoutNode` discriminated union, with a dedicated `PendingIssueLayoutNode` interface. `IsIssueNode` continues to test only for `kind === 'issue'`, and a new `isPendingIssueNode` guard handles the new kind.

**Rationale**: the alternative — overloading `IssueLayoutNode` with a sentinel id like `__pending__` — leaks string-magic into the type system and silently survives every `if (isIssueNode(node))` site without forcing a decision. With a real kind, every consumer that pattern-matches on the union is type-checked at build time. The engine itself (`GraphLayoutService`) is generic over `IGraphNode` and stays untouched. Phase rows are post-pass and don't use the pre-layout discriminator, so they don't bias this call.

**Synthetic node fields**: `kind: 'pending-issue'`, `id: '__pending-issue__'` (stable sentinel id used only for engine de-duplication; never exposed as a real id to the renderer), `childSequencing: 'series'` (no children at edit time), and the same `parentIssues?: readonly ParentIssueRef[]` slot the engine's `buildChildrenLookup` uses to slot the synthetic into the right children bucket of its parent.

### Decision 2 — State machine for o / shift+o + Tab / Shift+Tab

`pendingNewIssue` becomes `{ mode: 'sibling-below' | 'sibling-above' | 'child-of' | 'parent-of', referenceIssueId, title, viewMode }`. Every transition recomputes the synthetic's `parentIssues` (and fabricated sortOrder) from scratch based on the current mode and the reference issue.

```
                       TREE                                NEXT
   o (selected = S):
     start             child of   ─⭾→ child of S          parent of   ─⭾→ parent of S
     sibling-below S       reference   (cancel: ⇧⭾)       reference     (replaces S's
                                                                          old parent)
   ⇧o (selected = S):
     start             parent of  ─⇧⭾→ parent of S        child of   ─⇧⭾→ child of S
     sibling-above S      reference   (replaces S's       reference      (cancel: ⭾)
                                       old parent)
```

The `parent-of` transitions specifically REPLACE S's primary parent edge: post-transition, S's `parentIssues[0]` becomes `{ parentIssue: synthetic.id, sortOrder: <S's old sortOrder under its old parent> }`, and the synthetic's `parentIssues[0]` becomes S's old parent ref (taking S's old slot under its grandparent). The synthetic inherits S's old sortOrder so the layout slot is preserved.

**Rationale**: The mapping is symmetric — Tab is "deepen the relationship in the visual direction the new node was inserted" — and produces zero visual jumps in any of the four cases, because in tree mode parents render above children and in next mode parents render below children. The earlier alternative (Tab always = child-of-ref) was rejected because in next mode it would either flip `o`'s default visual position or force the synthetic upward through S, which was confusing.

**State graph constraints**:
- All transitions go through the default-sibling state. There is no direct child-of ↔ parent-of transition.
- The "wrong" key in the active sequence is a no-op (e.g. `o`+Shift+Tab does nothing; only Tab promotes).
- The "right" key after promotion cancels back to default-sibling; pressing it again is a no-op.

### Decision 3 — Fabricate sortOrder client-side, aligned with `Fleece.Core` ordinal-string sort keys

A new `src/Homespun.Web/src/features/issues/services/sort-order-midpoint.ts` exposes `midpoint(prev: string, next: string): string` returning a string strictly between `prev` and `next` under ordinal-byte comparison, matching the comparator at `issue-layout-service.ts:121-131`. The alphabet is the same one `Fleece.Core` uses (printable ASCII range used in its existing sort-key writer); when no midpoint exists at the current length, the function appends a midpoint character so the result lands strictly between the neighbours at one character longer. Boundary cases:

- `prev = ""` → return midpoint between `""` and `next` (i.e., a string < `next`).
- `next = ""` (no successor) → append a "high" alphabet character to `prev`.
- `prev === next` → fail-fast assertion (caller bug — should never happen with valid sibling ordering).

**Rationale**: The wire-level POST contract (`siblingIssueId` + `insertBefore`) lets the server pick the canonical sortOrder. The fabricated client value is purely a layout-preview concern. Aligning the algorithm with `Fleece.Core`'s writer means that when the SignalR echo arrives with the server's chosen sortOrder, the relative ordering matches what the client previewed, so the row does not jump between "submit" and "echo." Misalignment would cause visible flicker; alignment is cheap to test.

**Parity test**: a small Vitest fixture compares 30-50 sample neighbour pairs against C#-emitted reference outputs. The C# reference test lives next to the existing layout fixtures and is opt-in (no UPDATE flag dance — just a static fixture committed to the repo).

### Decision 4 — Synthetic enters the pipeline at the `LayoutIssue[]` boundary

`computeLayoutFromIssues({ issues, ..., pendingIssue? })` is the single entry point. The driver builds `layoutIssues` from `issues`, then if `pendingIssue` is present, appends a synthetic `LayoutIssue`-shaped record (status `'open'`, fabricated id, fabricated `parentIssues`). It also patches the reference's `parentIssues` for the `parent-of` cases (S's parent edge now points at the synthetic instead). The patched `LayoutIssue[]` is fed to `IssueLayoutService.layoutForTree` / `layoutForNext`.

**Rationale**: the alternative — injecting after `runEngine` returns positioned nodes — makes the engine's lane / row assignment ignore the synthetic, defeating the entire goal. The alternative — passing the synthetic *as a `LayoutNode`* directly to a hypothetical lower-level `runEngineWithExtraNodes` — would expose the engine internals through a second public API for one caller. Injecting at the `LayoutIssue` layer keeps a single engine entry path and makes the synthetic appear in `display`, `displayLookup`, and `childrenOf` automatically.

**Reference patching**: the patch is applied to a *copy* of the reference issue, never the cached `IssueResponse`. The driver builds a `Map<id, LayoutIssue>` after copying, so the `parentIssues` mutation never escapes the layout call.

### Decision 5 — Filter / search / actionable bypass for synthetic

The post-layout filter pass at `task-graph-view.tsx:216-236` short-circuits on `isPendingIssueRenderLine(line) → true`. Same for the next-mode `marker !== Actionable` filter at line 224 and any future filter dimensions. Selection by arrow keys does NOT include the synthetic (it owns its own keyboard input); scroll-into-view DOES include it (so the user always sees what they're editing, even if the parent issue would normally have been off-screen).

**Rationale**: the synthetic is a UI affordance, not a data row — filters are about narrowing the view of *real* data, not hiding the user's in-progress action. The opposite (filter applies to synthetic) would have it disappear mid-edit if the user is in a filtered view, which is hostile.

### Decision 6 — Focus and shortcut suppression

`use-toolbar-shortcuts` checks `document.activeElement` for the `<InlineIssueEditor>` root before dispatching to `onCreateAbove` / `onCreateBelow`. When focus is inside the editor, `o` / `shift+o` / arrow keys / Tab / Shift+Tab / Enter / Escape all stay with the input. The synthetic's row dispatch wraps the `<InlineIssueEditor>` in an `onKeyDown` handler that owns Tab / Shift+Tab and forwards to the state machine; Enter commits; Escape cancels.

### Decision 7 — Single pending state

`pendingNewIssue` is a single state slot. Pressing `o` / `shift+o` while editing types into the input (those keys never bubble out of the focused editor; see Decision 6). There is no UI path to two simultaneous synthetics. Cancel (Escape) clears the slot; Enter resolves the slot via the existing `useCreateIssue` mutation, which on success clears the slot when the SignalR echo merges the real issue.

## Risks / Trade-offs

- **Layout cost on every keypress** → re-running `computeLayoutFromIssues` on every Tab/Shift+Tab is a non-issue (it's a memoised pure function over a small set, ms-scale even on large graphs), but typing the title doesn't need to re-layout. **Mitigation**: title text isn't part of the layout-affecting state — keep title in a separate piece of `pendingNewIssue` from the `mode` field, and only include `mode` (plus reference id and viewMode) in the `useMemo` dep tuple. Title changes go straight to the input element via the controlled-component pattern, no layout rerun.
- **Server picks a different sortOrder than the client preview** → row jumps between submit and echo. **Mitigation**: align the midpoint-string algorithm with the `Fleece.Core` writer (Decision 3) and parity-test a sample. Worst-case visual jump is still bounded to one slot.
- **Synthetic introduces a cycle by accident** → `findParentCycle` runs in `runEngine`. The synthetic is always a leaf in `childrenOf` (no real children), so it cannot form a cycle even in the `parent-of` reparenting case (S becomes synthetic's child; synthetic has S's old parent, which is upstream of synthetic, not downstream). **Mitigation**: a unit test covers the reparenting case; if a cycle ever did slip through the engine returns `{ ok: false, cycle }` and the renderer falls back to the existing degraded-mode banner.
- **Synthetic in a filtered/empty next-mode view** → if the user creates a pending where the chosen reference is the only ancestor of an empty `matchedIds` set, the seed-from-actionable fallback might produce a different layout than expected. **Mitigation**: a Vitest case covers `layoutForNext` with an empty seed plus a synthetic — the synthetic should always render; the seed-fallback to `layoutForTree` covers the case naturally.
- **Reference issue is a phase row** → phase rows block hierarchy hotkeys today. With the synthetic flow, `o` / `shift+o` on a phase row already short-circuits via the existing phase-row guard. No new code needed; covered by an existing test.
- **Type-system audit cost** → introducing a new `LayoutNode` kind ripples through every `isIssueNode` / `isIssueRenderLine` site. **Mitigation**: TypeScript will flag every site that needs to consider the new kind; spec-driven specs include scenarios that fail the build if a site silently drops the synthetic.

## Migration Plan

This is a UI-only behavioural change with no API or wire-format impact and no persistent state migration. Rollout is a single PR. Rollback is `git revert`. There is no feature flag — the new flow is fully gated behind the existing `pendingNewIssue` state, and the cleanup in this PR removes the legacy `renderInlineEditor()` path entirely (no dual-path period).

## Open Questions

None outstanding. All decisions captured above. Implementation proceeds straight to tasks.md.
