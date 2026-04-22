## Context

The task-graph's orphan handling accumulated its current shape in two steps: the bottom "Orphaned Changes" section shipped first for main-branch orphans, then per-branch inline lists were added later to expose the `[Link to issue]` and `[Create sub-issue]` actions. The result is that the same orphan can appear in two places (inline under a branch-owning issue *and* at the bottom when it also exists on main), and the graph body carries secondary-content rows that break its vertical rhythm.

The design goal of this change is to treat "orphan change" as a first-class object in its own section with actionable UX for linking, and to keep the graph body focused on issues + PRs.

A subtlety drove the fan-out decision: the user reasonably assumes the link is a semantic relation between an issue and a change name. In practice, `POST /api/openspec/changes/link` writes a `.homespun.yaml` sidecar file into the exact working clone where `openspec/changes/<name>/` physically exists (`ChangeSnapshotController.cs:135-142`). Passing `branch: null` resolves to the main clone; if the change directory only exists on a feature branch, the endpoint returns 404. The dedup-by-name UI model and the branch-located server reality are reconciled by having the client carry an occurrence list and fan out one call per occurrence.

## Goals / Non-Goals

**Goals:**

- Remove the inline branch-scoped orphan list from the task graph.
- Provide a single deduped "Orphaned Changes" section at the bottom with occurrence-aware rows.
- Offer a discoverable link target via a filterable issue picker that highlights containing-branch issues.
- Share the issue-row visual between the graph and the picker without coupling their shells (so the picker does not render an SVG gutter, and the graph row does not lose its keyboard / action surface).

**Non-Goals:**

- Server-side refactor of the link endpoint (e.g. accepting a branchless form and auto-discovering clones). Worth considering later; out of scope here.
- Changing the `.homespun.yaml` sidecar schema.
- Changing what counts as an orphan (unchanged: a change directory with no sidecar).
- Reworking the "Create sub-issue" button into a full inline-hierarchy editor — the split-button picker is the thin reuse of the existing picker.
- Fuzzy-matching on fields other than title (description, assignee, tags).

## Decisions

### D1: Dedup by change name; carry an occurrence list per row

**Decision:** The bottom list emits one row per unique change name. Each row owns an `occurrences: { branch: string | null, changeName: string }[]` collection assembled from `mainOrphanChanges` (producing a `branch: null` entry) and from every `openSpecStates[issueId].orphans` match.

**Rationale:** Users think about "the change called X"; the server stores its link as a sidecar in each clone that carries the change directory. Deduping at the display layer matches the user's mental model; carrying the occurrence list preserves what the server needs.

**Alternatives considered:**

- *One row per `(branch, name)` pair (no dedup)* — honest to the backing storage but produces visibly duplicate names when the same orphan lives on main + a feature branch. Rejected.
- *Server-side dedup + branchless link endpoint* — cleaner, but out of scope; would require scanning every tracked clone on link. Tracked as a potential follow-up.

### D2: Count-only "on N branches" label with tooltip

**Decision:** Where an orphan row has more than one occurrence, render a muted "on N branches" label; the full branch-name list is available via `title`/tooltip. For single occurrences, render either "main" or the single branch name directly.

**Rationale:** Full branch names wrap ugly when several are present; a count + tooltip communicates the "this lives in multiple places" signal without sacrificing row density. The picker conveys the same information more richly via highlighting.

### D3: Link mutation fans out across occurrences

**Decision:** `useLinkOrphan` accepts `{ projectId, occurrences, fleeceId }` and emits one `POST /api/openspec/changes/link` per occurrence, awaiting all before invalidating `['task-graph', projectId]`. Any individual call failure causes the mutation to reject; the hook reports a single error back to the caller, and already-succeeded sidecars remain in place (best-effort, not transactional).

**Rationale:** The server writes one sidecar per clone; there is no transactional boundary spanning clones. A partial success leaves the system in a correct intermediate state (some occurrences linked, others orphan) that the next graph refresh will surface honestly. Introducing client-side compensation would add complexity for a rare failure mode.

**Alternatives considered:**

- *Send a single call with a `branches: string[]` field* — requires a server API change. Out of scope.
- *Pick the "primary" branch (e.g. first) and link only that occurrence* — under-reports the user's intent; the other occurrences would keep showing up in the orphan list and invite confusion.

### D4: Highlights render as pinned duplicates, without the graph chrome

**Decision:** In the picker, issues whose branch contains the orphan (computed from `openSpecStates`) appear first in the list, in their original order, followed by a divider, followed by the complete list filtered by the fuzzy search. Pinned rows render the same way as non-pinned rows (`IssueRowContent` in a picker shell). Duplication is intentional so a fuzzy-filter query that matches the highlighted issue does not make it disappear from the pinned block.

**Rationale:** Pinning surfaces the "probably what you want" targets without hiding the broader set. Duplicate-display keeps filter results predictable — the pinned block is orthogonal to the filtered list.

### D5: Extract `IssueRowContent` rather than add a `variant` prop to `TaskGraphIssueRow`

**Decision:** Factor the center content strip of `TaskGraphIssueRow` (type-dropdown → status-dropdown → OpenSpec indicators → phase-rollup → execution-mode-toggle → multi-parent badge → title → assignee → linked-PR) into a standalone `IssueRowContent`. Both the graph row and the picker row wrap it with their own outer shells: the graph row keeps its SVG gutter + `IssueRowActions` + keyboard/aria surface; the picker row uses a minimal `<button>`-role wrapper with click-to-select semantics and no hover actions.

**Rationale:** The alternative — a `variant: 'graph' | 'picker'` prop that conditionally nukes chunks — creates long ternary chains and couples two disjoint interaction models inside one component. Sharing the content subcomponent keeps the *visual* row identical (the stated directive) while letting the shells own their distinct behavior.

**Trade-off:** Props drilling. Type/status dropdown callbacks and execution-mode toggle stay on the graph-row shell (the picker does not need them). `IssueRowContent` takes an `editable?: boolean` prop that switches the type/status/exec-mode controls from interactive dropdowns to static pills; this is the minimum coupling required to share the visual.

### D6: Create-issue split button reuses the picker for the sub-issue path

**Decision:** The split button's primary action (`Create issue`) behaves as today — creates a top-level issue titled `OpenSpec: <changeName>`, then fans out the link across occurrences. The secondary action (`Create as sub-issue under…`) opens the *same picker dialog* in a "choose parent" mode; selecting an issue creates a new child under it (via `useCreateIssue` with `parentIssueId`) and then links. The picker dialog takes a callback prop so the same component serves both modes.

**Rationale:** One dialog, one keyboard model, one filter UX; the picker body is invariant across link-target and parent-target semantics. Only the dialog title, the highlight rule, and the commit callback differ.

**Trade-off:** In "choose parent" mode, the highlight rule is less obviously useful (containing-branch issues are not inherently better parent candidates than others). Acceptable — the rule still surfaces "issues already related to this change."

### D7: Plain fuzzy substring filter, not the advanced filter-query parser

**Decision:** The picker's filter input does a case-insensitive substring match over issue titles. It does not reuse `filter-query-parser.ts` (which supports `status:`, `type:`, `priority:`, `assignee:`, `me`, `isNext:`).

**Rationale:** Picker interactions want cheap, predictable, forgiving matching. The advanced parser's syntax is overkill for the narrow "find the issue I mean" use case and would invite support questions about why `isNext:true` behaves differently here than on the task graph.

## Risks / Trade-offs

- **Risk: partial link failure leaves a multi-occurrence orphan half-linked.** Rare (N>1 is the duplicate-across-clones case) but possible. Mitigation: surface the error; the next task-graph refresh shows the remaining occurrences still as orphan. Acceptable given the alternative is transactional complexity across clones.
- **Trade-off: the picker re-renders the full issue list for every dialog open.** For projects with hundreds of issues this is unimportant (react keyed lists, no virtualization needed at that size), but if issue counts grow past ~1k the picker should switch to a virtualized list. Out of scope now.
- **Risk: `IssueRowContent` extraction regresses the graph row visually.** Mitigation: the extraction is a pure refactor (same JSX, one level of indirection); component snapshot tests catch pixel drift; the graph row's wrapping shell is unchanged.
- **Trade-off: users who relied on the inline `[Create sub-issue]` proximity lose a click of context.** The split button keeps the capability; the picker adds discoverability. Net-positive for the common case.

## Open Questions

- **When N>1 occurrences exist, should the picker show a "this will link N sidecars" confirmation before committing?** Probably no — the count is already visible on the orphan row, and the picker is a single-click commit today. Working assumption: no extra confirmation; error messaging covers partial failure.

## Follow-ups

- **Server-side branchless link endpoint.** The fan-out in D3 is a workaround for the fact that the link endpoint targets one clone at a time. A branchless form that auto-discovers every clone carrying the change directory would collapse the fan-out to a single server call and remove the partial-failure mode. Tracked as Fleece issue `gxot0L` (filed alongside this proposal).
