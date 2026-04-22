## MODIFIED Requirements

### Requirement: Virtual sub-issue rendering from tasks.md

The system SHALL parse `tasks.md` from linked changes and render one virtual sub-row per phase heading directly in the issue task graph. Phase rows SHALL be full participants in the graph for selection and expand interactions but SHALL NOT support edit, dispatch, move, or hierarchy actions.

#### Scenario: Phase-level virtual row

- **WHEN** an issue has a linked OpenSpec change whose `tasks.md` contains `## <Phase Name>` headings with checkbox tasks
- **THEN** the graph SHALL emit one render line of type `phase` immediately under the issue's render line for each phase heading, ordered by their appearance in `tasks.md`
- **AND** each phase line SHALL display the phase name and `done/total` task counts
- **AND** each phase line SHALL be rendered with a diamond ◆ node shape so it is visually distinguishable from issue nodes (round ○ for issues without a linked change, square □ for issues with a linked change)
- **AND** the phase line SHALL be parented to the issue node above using the existing series-child connector geometry, regardless of the parent issue's `executionMode`

#### Scenario: Phase rows are full participants in selection and expand

- **WHEN** the user navigates with j/k arrow keys
- **THEN** keyboard selection SHALL move through phase rows just as it moves through issue rows
- **AND** double-clicking a phase row OR pressing ⏎ on a selected phase row SHALL toggle an inline detail panel directly under the phase row showing every leaf task with its checkbox state and description
- **AND** the inline detail panel SHALL grow to fit its task list with a `max-h` + scroll on the inner task list region rather than imposing a fixed expanded-row height

#### Scenario: Phase rows block edit, dispatch, move, and hierarchy actions

- **WHEN** a phase row is the selected row
- **THEN** the row SHALL render no editable type pill, status pill, execution-mode toggle, multi-parent badge, or `IssueRowActions`
- **AND** keyboard hotkeys for edit (`e`), run-agent (`r`), move (`m`/`M`), and hierarchy reparent SHALL silently no-op
- **AND** the only actions reachable from a phase row SHALL be select, expand/collapse, and (via parent navigation) move selection back to the issue row

#### Scenario: Phase synthesis is driven by OpenSpec state

- **WHEN** `computeLayout` runs over a `TaskGraphResponse`
- **THEN** it SHALL receive `openSpecStates` as part of its inputs
- **AND** for each issue render line whose `OpenSpecStates[issueId].Phases` is non-empty, it SHALL synthesise one `phase` render line per `PhaseSummary` in document order
- **AND** issues with no linked change OR a linked change with no phases SHALL produce no phase rows

#### Scenario: Modal-based phase detail flow is removed

- **WHEN** a user views an issue with a linked change in the graph
- **THEN** the legacy `PhaseRollupBadges` inline pill cluster and its `Dialog` modal SHALL NOT be rendered anywhere in the graph row content
- **AND** clicking on a phase row SHALL NOT open a modal dialog — the inline expansion is the only path to view phase tasks
