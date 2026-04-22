## Why

Orphan OpenSpec changes (those without a `.homespun.yaml` sidecar) currently surface in two places on the task graph:

1. An **inline list under each issue row** whose branch contains the orphan (`BranchOrphanList`, with `[Link to issue]` + `[Create sub-issue]` actions).
2. A **flat "Orphaned Changes" section at the bottom** for orphans that live on main (`MainOrphanList`, with a `[Create issue]` action).

The split design has two problems:

- **Screen clutter** — every issue whose branch carries an orphan grows a secondary list, so the task graph's vertical rhythm is interrupted repeatedly. The per-issue listing does not help users make linking decisions — they already know *which* branch the orphan lives on — and the same information is reachable by expanding the issue.
- **No discovery path for the non-obvious target** — the inline `[Link to issue]` only links to the *branch's own* issue. If the user wants to link the orphan to a different, semantically-related issue, there is no UI for it today.

The bottom list is the natural home for "changes needing an owner." It just needs two upgrades: the actions users will actually want, and a picker that surfaces the issues most likely to be the right target.

## What Changes

- **Remove `BranchOrphanList` inline rendering** from the task graph. Orphans no longer clutter the graph body.
- **Bottom list dedupes by change name.** The same orphan name appearing on `main` and/or one or more feature branches shows as a single row. Each row carries its full occurrence list (`{ branch, changeName }[]`) for the link call.
- **Count-only "found on N branches" label** with a tooltip listing the branch names. Avoids horizontal sprawl when an orphan lives on many branches.
- **Two actions per orphan row:**
  - `[🔗 Link to issue]` — opens a picker dialog (see below).
  - Split button: primary action `Create issue` (creates a new top-level issue and links), secondary action `Create as sub-issue under…` (opens the picker to choose a parent, creates a child under it, links).
- **Link-picker dialog:**
  - Plain fuzzy-text filter input at the top.
  - Body renders the full issue list using a new `IssueRowContent` subcomponent extracted from `TaskGraphIssueRow` — same type/status badges, OpenSpec indicators, title, but no SVG gutter, no multi-parent badge, no PR-status fetch, no hover actions.
  - Issues whose branch contains the orphan are **pinned at the top of the list as duplicates** (they also appear in their normal sorted position below). A divider separates the pinned block from the rest.
  - Clicking an issue row commits the link (or the create-sub-issue action when invoked from that path) and closes the dialog.
- **Link mutation fans out across occurrences.** Because the server-side link writes a `.homespun.yaml` sidecar into the specific clone where the change directory exists, the `useLinkOrphan` hook is changed to accept an occurrence list and emit one `POST /api/openspec/changes/link` call per `(branch, changeName)` pair. Typical case is N=1; N>1 is the rare cross-branch-duplicate case.
- **Extract `IssueRowContent`** — the center content strip of `TaskGraphIssueRow` (type dropdown → status dropdown → OpenSpec indicators → phase badges → execution mode → title → assignee → linked-PR → actions) becomes a shared component with two shells: the graph row (SVG gutter + all actions) and the picker row (no gutter, no actions, click-to-select).

## Capabilities

### Modified Capabilities

- `openspec-integration`: the "Orphan change handling" requirement is rewritten. Branch orphans no longer render inline under their issue; both main and branch orphans converge on the bottom list with a new link-picker action. Two new requirements are added covering the picker dialog and the per-occurrence link fan-out.

### New Capabilities

_None — this is a UX consolidation within `openspec-integration`._

## Impact

- **Frontend (all changes in `src/Homespun.Web/src/features/issues/`):**
  - `components/orphan-changes.tsx` — `BranchOrphanList` export removed; `MainOrphanList` becomes the sole renderer, renamed/restructured to render the deduped occurrence-aware rows with the new two-action layout.
  - `components/task-graph-view.tsx` — drops the `<BranchOrphanList>` invocation near line 1079; bottom section continues to render after the last issue row.
  - `components/task-graph-row.tsx` — center content strip is factored out into a new `components/issue-row-content.tsx`; the graph row re-wraps it with the SVG gutter + actions shell.
  - `components/orphan-link-picker.tsx` *(new)* — Dialog with fuzzy filter + pinned-highlights list + `IssueRowContent` rows.
  - `hooks/use-link-orphan.ts` — input shape changes from `{ projectId, branch, changeName, fleeceId }` to `{ projectId, occurrences: {branch, changeName}[], fleeceId }`; mutation body loops over occurrences and emits one POST each; cache invalidation unchanged.
  - `hooks/use-create-issue.ts` — used by the split-button paths; no interface change.
  - `services/orphan-aggregation.ts` *(new)* — pure function that takes `TaskGraphResponse` and produces `{ name, occurrences[], containingIssueIds[] }[]` for the bottom list.

- **Backend:** no changes. `POST /api/openspec/changes/link` contract stays as-is.

- **Shared contracts:** no DTO changes. `SnapshotOrphan` unchanged.

- **Testing:**
  - Unit tests for `orphan-aggregation.ts` (dedup, occurrence list, containing-issue lookup).
  - Unit tests for `use-link-orphan` fan-out (one call per occurrence; all must succeed; invalidation runs once).
  - Component tests for `orphan-link-picker.tsx` (fuzzy filter narrows, highlighted issues pinned + duplicated below, click commits).
  - Component tests for `IssueRowContent` (shared strip renders identically in both shells).
  - Playwright e2e updating `e2e/openspec-orphan-actions.spec.ts` (if exists) or new spec covering the picker flow end-to-end.

- **Removed:** the inline `[Create sub-issue]` action disappears from branch-row context; it is reborn as the split-button secondary action on the bottom row. Functionally equivalent; different entry point.

- **Rollback posture:** self-contained frontend change; revert reinstates the inline lists without server impact.
