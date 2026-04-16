## Purpose

Integrate OpenSpec change lifecycle (propose → apply → archive) with Homespun's Fleece-issue-driven agent workflow. Links changes to issues via `.homespun.yaml` sidecars, scans branches for change state, surfaces progress in the issue graph, and adds an OpenSpec tab to the run-agent panel with auto-selected skill dispatch.

## Requirements

### Requirement: Change-to-issue linkage via sidecar

The system SHALL link OpenSpec changes to Fleece issues via a `.homespun.yaml` sidecar file in the change directory.

#### Scenario: Sidecar contains fleece issue ID
- **WHEN** a change at `openspec/changes/<name>/` contains `.homespun.yaml`
- **THEN** it SHALL contain `fleeceId: <issue-id>` and `createdBy: server|agent`
- **AND** the scanner SHALL use this to determine the linked Fleece issue

#### Scenario: Server auto-writes sidecar post-session for orphan changes
- **WHEN** a session ends and the snapshot shows a new change without a sidecar
- **AND** exactly one orphan change exists on the branch
- **THEN** the server SHALL write `.homespun.yaml` with the branch's fleece-id and `createdBy: agent`

#### Scenario: Multiple orphan changes require UI disambiguation
- **WHEN** a session ends and multiple unlinked changes exist on the branch
- **THEN** the server SHALL NOT auto-link
- **AND** the UI SHALL surface each orphan with [link-to-issue] / [create-sub-issue] actions

#### Scenario: Sidecar survives archive
- **WHEN** a change is archived to `openspec/changes/archive/<dated>-<name>/`
- **THEN** the `.homespun.yaml` sidecar SHALL be preserved in the archive directory

### Requirement: Branch scanner service

The system SHALL scan branches for OpenSpec change state and make it available to the issue graph.

#### Scenario: Post-session snapshot
- **WHEN** an agent session ends on a branch
- **THEN** the worker SHALL scan `openspec/changes/` on the branch
- **AND** SHALL POST a snapshot to the server containing `{branch, fleeceId, changes[], phaseState}`
- **AND** the server SHALL cache this keyed by `(projectId, branch)`

#### Scenario: On-demand scan with cache fallback
- **WHEN** the UI requests graph data and no cached snapshot exists (or cache is stale beyond 60s TTL)
- **THEN** the server SHALL perform a live disk scan of the on-disk clone
- **AND** SHALL cache the result

#### Scenario: Inherited changes are filtered out
- **WHEN** a branch contains changes inherited from main (with sidecars pointing to other fleece-ids)
- **THEN** the scanner SHALL exclude them from the branch's linked changes
- **AND** only changes whose `fleeceId` matches the branch's fleece-id suffix SHALL be shown under that issue

#### Scenario: Archived change fallback
- **WHEN** a linked change is no longer in `openspec/changes/` but exists in `openspec/changes/archive/`
- **THEN** the scanner SHALL read the archived sidecar
- **AND** SHALL auto-transition the fleece issue to `complete` status

### Requirement: Issue graph change indicators

The system SHALL display branch and change status indicators on each issue row in the graph.

#### Scenario: Branch indicator colours
- **WHEN** an issue has no branch → gray branch symbol
- **WHEN** an issue has a branch but no change → white branch symbol
- **WHEN** an issue has a branch with a change → amber branch symbol

#### Scenario: Change status symbols
- **WHEN** no change exists → no symbol
- **WHEN** change exists, artifacts incomplete → red ◐
- **WHEN** all schema artifacts created → amber ◐
- **WHEN** all tasks checked → green ●
- **WHEN** change archived → blue ✓

#### Scenario: Issue node shape
- **WHEN** an issue has no linked change → round node (○)
- **WHEN** an issue has a linked change → square node (□)

### Requirement: Virtual sub-issue rendering from tasks.md

The system SHALL parse `tasks.md` from linked changes and render phase-level roll-ups in the issue graph.

#### Scenario: Phase-level roll-up
- **WHEN** tasks.md contains `## N. Phase Name` headings with checkbox tasks
- **THEN** the graph SHALL show one virtual sub-node per phase heading
- **AND** each sub-node SHALL display `done/total` task counts

#### Scenario: Phase detail modal
- **WHEN** the user clicks a phase badge
- **THEN** a modal SHALL display all leaf tasks under that phase with their checkbox state

#### Scenario: Phases are not individually dispatchable
- **WHEN** a virtual phase sub-node is rendered
- **THEN** it SHALL be display-only with no dispatch action

### Requirement: OpenSpec tab in run-agent panel

The system SHALL provide an "OpenSpec" tab in the run-agent panel that replaces the former "Workflow" tab.

#### Scenario: All 8 OpenSpec skills are listed
- **WHEN** the user opens the OpenSpec tab for a change-linked issue
- **THEN** the tab SHALL list all 8 OpenSpec skills
- **AND** each SHALL be selectable for dispatch

#### Scenario: Auto-selection defaults
- **WHEN** no change exists or artifacts are incomplete → default to `openspec-explore`
- **WHEN** all schema artifacts are created → default to `openspec-apply-change`
- **WHEN** all tasks in tasks.md are checked → default to `openspec-archive-change`

#### Scenario: Readiness gating for apply, verify, sync, archive
- **WHEN** the user selects `apply`, `verify`, `sync`, or `archive`
- **AND** their prerequisites are not met
- **THEN** the skill SHALL be visually marked as blocked
- **AND** SHALL NOT be dispatchable
- **AND** `explore`, `propose`, `new-change`, and `continue-change` SHALL always be available

#### Scenario: Schema override injection
- **WHEN** the project uses a non-default schema (per `openspec/config.yaml`)
- **THEN** the dispatch SHALL inject `"use openspec schema '{schema}' for all openspec commands"` into the session's system prompt

### Requirement: Orphan change handling

The system SHALL surface orphan changes (changes without sidecars) in the UI with actions to link or create issues.

#### Scenario: Orphan on agent branch
- **WHEN** an unlinked change exists on a branch associated with a fleece issue
- **THEN** it SHALL render as a virtual child under that issue in the graph
- **AND** SHALL offer [link-to-issue] and [create-sub-issue] actions

#### Scenario: Orphan on main branch
- **WHEN** an unlinked change exists on main
- **THEN** it SHALL render at the bottom of the graph in an "Orphaned Changes" section
- **AND** SHALL offer a [create-issue] action

### Requirement: Multi-change per branch

The system SHALL support multiple changes on a single branch, each linked to its own fleece issue via its own sidecar.

#### Scenario: Sibling changes under same issue
- **WHEN** multiple changes on a branch have sidecars pointing to the same fleece-id
- **THEN** the graph SHALL render each as a sibling node under that issue

#### Scenario: Changes linked to different issues on same branch
- **WHEN** changes on a branch have sidecars pointing to different fleece-ids
- **THEN** each change SHALL appear under its own respective issue in the graph
