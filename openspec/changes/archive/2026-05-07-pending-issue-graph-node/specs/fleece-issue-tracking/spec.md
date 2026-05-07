## ADDED Requirements

### Requirement: Pending issue rendered as a virtual layout participant

When the user is creating a new issue inline in the task graph (`o` / `shift+o`), the web client SHALL inject a synthetic "pending issue" node into the layout pipeline so that the issue's row, lane, and edges are computed by the same `IssueLayoutService` that lays out real issues. The synthetic SHALL be a discriminated `LayoutNode` of kind `'pending-issue'` (`PendingIssueLayoutNode`) defined in `src/Homespun.Web/src/features/issues/services/layout/nodes.ts` alongside the existing `'issue'` kind.

The synthetic SHALL:

- be the single source of truth for the in-progress new issue's hierarchy and visual position; the legacy `renderInlineEditor()` graft path that splices `<InlineIssueEditor>` into the row list at a fixed `insertAtIndex` SHALL be removed;
- carry `childSequencing: 'series'` (it has no children at edit time) and a `parentIssues` slot consumed by `IssueLayoutService.runEngine` for children-bucket placement;
- never be exposed to the layout-fixture parity tests in `tests/Homespun.Web.LayoutFixtures/` — synthetic injection is TS-only and the C# reference layout MUST NOT receive a `pending-issue` node;
- always render regardless of any active task graph filter, search query, or next-mode `marker !== Actionable` predicate.

The web client SHALL NOT introduce a parallel layout path or a second engine entry point for the synthetic. `computeLayoutFromIssues({ issues, …, pendingIssue? })` SHALL remain the single layout entry point; injection happens by appending a synthetic `LayoutIssue` to the engine input.

#### Scenario: synthetic is positioned as sibling-below in default state
- **WHEN** the user presses `o` while issue `S` is selected
- **THEN** `computeLayoutFromIssues` SHALL be called with `pendingIssue` describing `mode = 'sibling-below'` referencing `S`
- **AND** the synthetic SHALL appear in the rendered layout immediately below `S` with the same parent and lane as a sibling of `S`
- **AND** the synthetic's row SHALL be rendered by mounting `<InlineIssueEditor>` at the engine-assigned row/lane

#### Scenario: synthetic node never appears in golden fixtures
- **WHEN** the layout-fixture parity tests under `tests/Homespun.Web.LayoutFixtures/` run
- **THEN** none of the input fixtures SHALL contain a `pending-issue` node
- **AND** the C# reference layout SHALL NOT be expected to emit `pending-issue` output

#### Scenario: filter / search / actionable bypass keeps synthetic visible
- **WHEN** an active filter or search query reduces the visible set, OR the view is in next mode and the synthetic's reference is non-actionable
- **THEN** the post-layout filter pass SHALL preserve any render line where `isPendingIssueRenderLine(line)` is true
- **AND** the synthetic SHALL render and SHALL be scrolled into view

#### Scenario: legacy renderInlineEditor graft path is removed
- **WHEN** the codebase is built with this change applied
- **THEN** there SHALL be no `renderInlineEditor()` function (or equivalent DOM-graft path) that splices `<InlineIssueEditor>` into the row list outside the layout-engine output
- **AND** the `PendingNewIssue` type SHALL NOT contain `pendingChildId` or `pendingParentId` fields

### Requirement: Hierarchy state machine for inline issue creation

The web client SHALL drive synthetic-node hierarchy via a 3-state-per-`o`-press machine. The state machine values, transitions, and per-mode mappings SHALL be exactly:

```
TREE MODE
  o          : sibling-below S  ─⭾→ child-of S   (cancel: ⇧⭾ → sibling-below S)
  ⇧o         : sibling-above S  ─⇧⭾→ parent-of S  (cancel: ⭾  → sibling-above S)
                                       (replaces S's primary parent edge)

NEXT MODE
  o          : sibling-below S  ─⭾→ parent-of S   (cancel: ⇧⭾ → sibling-below S)
                                       (replaces S's primary parent edge)
  ⇧o         : sibling-above S  ─⇧⭾→ child-of S   (cancel: ⭾  → sibling-above S)
```

For every transition, the web client SHALL recompute the synthetic's `parentIssues[]` and (when entering a `parent-of` state) patch a copy of the reference issue's `parentIssues[]` so that `S` becomes a child of the synthetic and the synthetic inherits `S`'s prior slot under its old parent. The reference patch SHALL be local to the layout call — it MUST NOT mutate the cached `IssueResponse`.

All other key combinations while editing SHALL be no-ops (e.g. `o` then `Shift+Tab` from the default sibling state does nothing). Pressing the active promotion key a second time SHALL also be a no-op (e.g. `Tab` after entering `child-of` does not deepen further).

#### Scenario: Tree mode `o + Tab` makes synthetic a child of S
- **WHEN** the user presses `o` while `S` is selected, then presses `Tab`
- **THEN** the synthetic's `parentIssues[0].parentIssue` SHALL equal `S.id`
- **AND** the synthetic SHALL render below `S` indented as a child of `S`
- **AND** there SHALL be no visual jump in the synthetic's row position compared to its sibling-below default

#### Scenario: Tree mode `Shift+O + Shift+Tab` reparents S under synthetic
- **GIVEN** issue `S` has a parent `P` with sortOrder `s_old` under `P`
- **WHEN** the user presses `Shift+O` while `S` is selected, then presses `Shift+Tab`
- **THEN** the synthetic's `parentIssues[0]` SHALL equal `{ parentIssue: P.id, sortOrder: s_old }` (taking S's old slot)
- **AND** the patched `S` (in the layout-only copy) SHALL have `parentIssues[0] = { parentIssue: synthetic.id, sortOrder: <fabricated> }`
- **AND** the synthetic SHALL render above `S` as `S`'s parent

#### Scenario: Next mode `o + Tab` makes synthetic the parent of S
- **GIVEN** the view mode is Next and issue `S` has a parent `P` with sortOrder `s_old` under `P`
- **WHEN** the user presses `o` while `S` is selected, then presses `Tab`
- **THEN** the synthetic's `parentIssues[0]` SHALL equal `{ parentIssue: P.id, sortOrder: s_old }`
- **AND** `S` SHALL be reparented under the synthetic
- **AND** the synthetic SHALL render below `S` (consistent with next-mode parent placement)

#### Scenario: Next mode `Shift+O + Shift+Tab` makes synthetic a child of S
- **WHEN** the view mode is Next and the user presses `Shift+O` while `S` is selected, then presses `Shift+Tab`
- **THEN** the synthetic's `parentIssues[0].parentIssue` SHALL equal `S.id`
- **AND** the synthetic SHALL render above `S` (consistent with next-mode child placement)

#### Scenario: Cancel-back-to-sibling reverts the synthetic
- **GIVEN** the synthetic is in any `child-of` or `parent-of` state
- **WHEN** the user presses the cancel key for the active sequence (`Shift+Tab` after `o+Tab`, `Tab` after `Shift+O+Shift+Tab`)
- **THEN** the synthetic SHALL revert to the corresponding default `sibling-below` or `sibling-above` state
- **AND** any reference-issue patch from a prior `parent-of` transition SHALL be undone

#### Scenario: Wrong-direction key from default state is a no-op
- **GIVEN** the synthetic is in `sibling-below` state after pressing `o`
- **WHEN** the user presses `Shift+Tab`
- **THEN** the synthetic state SHALL NOT change and the layout SHALL NOT be re-run

#### Scenario: Repeat promotion key is a no-op
- **GIVEN** the synthetic is in `child-of` state
- **WHEN** the user presses `Tab` again
- **THEN** the synthetic state SHALL NOT change and the layout SHALL NOT be re-run

### Requirement: Client-side ordinal-string sortOrder midpoint aligned with Fleece.Core

The web client SHALL fabricate a `sortOrder` string for the synthetic's parent edge using a midpoint-string algorithm that produces values matching `Fleece.Core`'s ordinal-string sort-order writer for any input pair. The algorithm SHALL be exposed at `src/Homespun.Web/src/features/issues/services/sort-order-midpoint.ts` as `midpoint(prev: string, next: string): string` and SHALL satisfy:

- `prev < midpoint(prev, next) < next` under the same ordinal-byte comparator the layout uses (`issue-layout-service.ts:121-131`);
- if no midpoint exists at the current length (adjacent codepoints), the result SHALL be one character longer with a midpoint character appended;
- `prev = ""` produces a value strictly less than `next`;
- `next = ""` (no successor) produces a value strictly greater than `prev`;
- `prev === next` SHALL fail-fast (caller bug).

The fabricated value SHALL be used only for the client-preview layout. The wire-level POST SHALL continue to send `parentIssueId` + `siblingIssueId` + `insertBefore`; the server retains authority over the canonical sortOrder.

#### Scenario: midpoint produces a value strictly between neighbours
- **WHEN** `midpoint("a", "c")` is called
- **THEN** the result SHALL satisfy `"a" < result < "c"` under ordinal-byte comparison

#### Scenario: midpoint extends length when codepoints are adjacent
- **WHEN** `midpoint("a", "b")` is called
- **THEN** the result SHALL be a string of length 2 or more whose first character is `"a"` and whose remainder lands strictly between empty-suffix and `"b"`'s suffix at the comparator level

#### Scenario: parity with Fleece.Core writer for sample pairs
- **WHEN** the parity test runs against a fixed sample of 30+ pairs
- **THEN** the TS implementation SHALL produce the same value the C# `Fleece.Core` writer would emit for the same insertion request, byte-for-byte

#### Scenario: client and server preview agree on relative ordering
- **GIVEN** the user creates a pending issue between siblings `A` and `B`
- **WHEN** the SignalR `IssueChanged` echo arrives carrying the server's chosen sortOrder
- **THEN** the relative ordering of the new issue versus `A` and `B` SHALL match the client preview
- **AND** the rendered row position SHALL NOT jump

### Requirement: Editor focus suppresses global graph shortcuts

While the synthetic editor's input element holds keyboard focus, the global toolbar shortcut hook (`use-toolbar-shortcuts`) SHALL NOT dispatch `o` / `shift+o` / arrow / Tab / Shift+Tab events to the graph navigation handlers. Those keys SHALL flow into the input or the synthetic's local `onKeyDown` handler instead.

The synthetic's own `onKeyDown` handler SHALL own Tab / Shift+Tab (drive the state machine), Enter (commit via `useCreateIssue`), and Escape (cancel and clear `pendingNewIssue`).

There SHALL be at most one synthetic at any time. Pressing `o` or `shift+o` while editing SHALL type the literal character into the title input; it SHALL NOT spawn a second synthetic.

#### Scenario: typing `o` in the editor inserts the character
- **GIVEN** the synthetic editor is focused and the title is empty
- **WHEN** the user presses `o`
- **THEN** the title SHALL become `"o"`
- **AND** no second synthetic SHALL be created

#### Scenario: arrow keys move the cursor inside the editor
- **GIVEN** the synthetic editor is focused with a multi-character title
- **WHEN** the user presses Left or Right arrow
- **THEN** the input cursor SHALL move within the title text
- **AND** the graph selection SHALL NOT change

#### Scenario: Escape cancels and clears the synthetic
- **WHEN** the user presses Escape while editing
- **THEN** `pendingNewIssue` SHALL be set to `null`
- **AND** the synthetic SHALL be removed from the next layout result
- **AND** focus SHALL return to the graph container

#### Scenario: Enter commits via the existing create-issue mutation
- **WHEN** the user presses Enter while editing with a non-empty title
- **THEN** `useCreateIssue` SHALL be called with `parentIssueId` + `siblingIssueId` + `insertBefore` derived from the synthetic's current state-machine state
- **AND** on success the synthetic SHALL be cleared and the SignalR-echoed real issue SHALL appear in the layout in the same relative position
