## ADDED Requirements

### Requirement: Sidebar lists running sessions grouped by project

The web client sidebar SHALL list every running session under its owning project, sourced from a single client-side query of all sessions, grouped client-side by `projectId`.

A "running session" SHALL be defined as any session whose `status` is one of: `STARTING`, `RUNNING_HOOKS`, `RUNNING`, `WAITING_FOR_INPUT`, `WAITING_FOR_QUESTION_ANSWER`, `WAITING_FOR_PLAN_EXECUTION`, or `ERROR`. Sessions with `status` `STOPPED` SHALL NOT be rendered in the sidebar.

#### Scenario: Sessions are grouped under their owning project
- **WHEN** the sidebar renders with two projects A and B, and three sessions whose `projectId` values are A, A, B respectively
- **THEN** project A's row SHALL render two session children
- **AND** project B's row SHALL render one session child
- **AND** no session SHALL appear under any other project

#### Scenario: STOPPED sessions are not shown
- **WHEN** the session list contains a session with `status` `STOPPED`
- **THEN** that session SHALL NOT render anywhere in the sidebar

#### Scenario: ERROR sessions are shown
- **WHEN** the session list contains a session with `status` `ERROR`
- **THEN** that session SHALL render in the sidebar under its project

#### Scenario: Session belongs to an unknown project
- **WHEN** a session's `projectId` does not match any known project
- **THEN** that session SHALL NOT render in the sidebar
- **AND** no error or warning SHALL be surfaced to the user

### Requirement: Sessions are sorted oldest-first by createdAt

Within each project group, sessions SHALL be sorted by `createdAt` ascending (oldest at the top, newest at the bottom).

#### Scenario: Oldest session appears at the top of its group
- **WHEN** project A has three sessions with `createdAt` values of 09:00, 10:00, and 11:00
- **THEN** the 09:00 session SHALL render first (top)
- **AND** the 10:00 session SHALL render second
- **AND** the 11:00 session SHALL render last (bottom)

#### Scenario: Sessions across different projects do not influence each other's sort
- **WHEN** project A has a session created at 09:00 and project B has a session created at 08:00
- **THEN** within project A's group, A's 09:00 session SHALL appear at the top of A's group
- **AND** within project B's group, B's 08:00 session SHALL appear at the top of B's group

### Requirement: Each session row shows status colour dot and truncated title

Each session row SHALL render exactly: a coloured status dot, followed by the session title truncated to fit the sidebar width. No mode badge, age, cost, or other metadata SHALL be rendered on the row.

The status → colour mapping SHALL be:
- `RUNNING`, `RUNNING_HOOKS`, `STARTING` → green
- `WAITING_FOR_INPUT` → yellow
- `WAITING_FOR_QUESTION_ANSWER` → purple
- `WAITING_FOR_PLAN_EXECUTION` → orange
- `ERROR` → red

This mapping SHALL be the same map used by the top-bar `ActiveAgentsIndicator`, sourced from a single shared utility.

#### Scenario: Running session renders a green dot
- **WHEN** a session has `status` `RUNNING`
- **THEN** the row SHALL render a green status dot

#### Scenario: Waiting-for-input session renders a yellow dot
- **WHEN** a session has `status` `WAITING_FOR_INPUT`
- **THEN** the row SHALL render a yellow status dot

#### Scenario: Error session renders a red dot
- **WHEN** a session has `status` `ERROR`
- **THEN** the row SHALL render a red status dot

#### Scenario: Long titles are truncated
- **WHEN** a session's title is longer than the available row width
- **THEN** the visible title SHALL be truncated with an ellipsis
- **AND** the full title SHALL be available via a hover tooltip

#### Scenario: Sidebar and top-bar share the same colour map
- **WHEN** the sidebar and the top-bar `ActiveAgentsIndicator` both render a session with the same `status`
- **THEN** the colour shown by both SHALL be identical
- **AND** both surfaces SHALL import the colour from the same shared utility module

### Requirement: Each project row is collapsible with a chevron

Each project row in the sidebar SHALL render as a collapsible group (using shadcn's `Collapsible` primitive) when it has at least one running session. The trigger SHALL include a visible chevron icon and SHALL expose `aria-expanded` reflecting current state.

A project with zero running sessions SHALL render as a single non-collapsible row — no chevron, no expand affordance, no children.

The collapse/expand transition SHALL be instant (no animation).

#### Scenario: Project with sessions has a chevron
- **WHEN** a project has at least one running session
- **THEN** the project row SHALL render a chevron trigger
- **AND** the trigger SHALL have an `aria-expanded` attribute

#### Scenario: Project with zero sessions has no chevron
- **WHEN** a project has zero running sessions (after STOPPED is filtered out)
- **THEN** the project row SHALL NOT render a chevron
- **AND** the project row SHALL render unchanged from its existing pre-feature appearance

#### Scenario: Clicking the chevron toggles visibility
- **WHEN** a project's session list is expanded and the user clicks the chevron
- **THEN** the session list SHALL collapse
- **AND** `aria-expanded` SHALL become `false`

#### Scenario: Collapse and expand are instant
- **WHEN** a project's session list toggles between expanded and collapsed
- **THEN** the transition SHALL complete in a single frame with no animation

### Requirement: Default expansion state is expanded, persisted per-project in localStorage

Each project's collapse state SHALL default to expanded (`true`) when the user has not previously toggled it. User toggles SHALL be persisted in `localStorage` under the key `homespun.sidebar.project-expanded.<projectId>`. The persisted state SHALL survive full page reloads.

`localStorage` failures (e.g. quota exceeded, blocked in private mode) SHALL be silently handled — the sidebar SHALL fall back to the default expanded state without surfacing an error to the user.

#### Scenario: First visit shows project expanded
- **WHEN** a project has at least one session and `localStorage` has no entry for that project
- **THEN** the project SHALL render expanded

#### Scenario: User toggle persists across reload
- **WHEN** the user collapses a project and then reloads the page
- **THEN** that project SHALL render collapsed on the next mount
- **AND** other projects' states SHALL be unaffected

#### Scenario: localStorage failure does not surface an error
- **WHEN** writing to `localStorage` throws (e.g. quota exceeded, private-mode block)
- **THEN** the toggle SHALL still update the in-memory state for the current session
- **AND** no error SHALL surface to the user

### Requirement: Clicking a session row navigates to that session's page

Each session row SHALL be a TanStack Router typed `Link` with target `/sessions/$sessionId`, populating `sessionId` with that session's `id`.

#### Scenario: Click navigates to the session's page
- **WHEN** the user clicks a session row in the sidebar
- **THEN** the application SHALL navigate to `/sessions/<that session's id>`

#### Scenario: Link is constructed via the typed router
- **WHEN** the session row is rendered
- **THEN** it SHALL use the typed `Link` component with `to="/sessions/$sessionId"` and `params={{ sessionId: session.id }}`

### Requirement: Sidebar session list updates live via SignalR without manual refresh

The sidebar session list SHALL update automatically in response to every Claude session SignalR lifecycle event, with no user-initiated refresh. The new `useAllSessions()` query SHALL be invalidated by the existing `invalidateAllSessionsQueries` helper invoked from `useGlobalSessionsSignalR`.

The events that SHALL trigger an invalidation are: `SessionStarted`, `SessionStopped`, `SessionStatusChanged`, `SessionError`, `SessionResultReceived`, and `SessionModeModelChanged`.

#### Scenario: SessionStarted adds a row
- **WHEN** the server emits `SessionStarted` for a new session in project A
- **THEN** within one query cycle the sidebar SHALL render a new row under project A
- **AND** no manual user action SHALL be required

#### Scenario: SessionStatusChanged updates the dot colour
- **WHEN** a session transitions from `RUNNING` to `WAITING_FOR_INPUT`
- **AND** the server emits `SessionStatusChanged`
- **THEN** the sidebar dot for that session SHALL change from green to yellow within one query cycle

#### Scenario: SessionStopped removes the row
- **WHEN** a running session transitions to `STOPPED`
- **AND** the server emits `SessionStopped`
- **THEN** the sidebar SHALL no longer render that session row

#### Scenario: All lifecycle events trigger invalidation
- **WHEN** any of `SessionStarted`, `SessionStopped`, `SessionStatusChanged`, `SessionError`, `SessionResultReceived`, or `SessionModeModelChanged` fires on the SignalR hub
- **THEN** the `useAllSessions()` query SHALL be invalidated
- **AND** the sidebar SHALL re-render with the latest data
