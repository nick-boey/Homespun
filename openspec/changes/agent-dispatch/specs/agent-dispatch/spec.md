## ADDED Requirements

### Requirement: Single-issue dispatch with atomic deduplication

The system SHALL dispatch an agent against a single issue via `POST /api/issues/{issueId}/run`, returning `202 Accepted` immediately while the dispatch pipeline runs in the background.

#### Scenario: Successful dispatch returns 202
- **WHEN** no session is active for the issue
- **THEN** the server SHALL atomically mark the issue as "starting" via `IAgentStartupTracker`
- **AND** SHALL queue an `AgentStartRequest` on `IAgentStartBackgroundService`
- **AND** SHALL return `202 Accepted` with `RunAgentAcceptedResponse`

#### Scenario: Duplicate dispatch returns 409
- **WHEN** a session is already running for the issue
- **THEN** the response SHALL be `409 Conflict` with `AgentAlreadyRunningResponse`

#### Scenario: Dispatch failure broadcasts AgentStartFailed
- **WHEN** the background pipeline fails or times out (5-minute budget)
- **THEN** `IAgentStartupTracker.MarkAsFailed` + `Clear` SHALL be called
- **AND** `AgentStartFailed(issueId, projectId, error)` SHALL be broadcast over `NotificationHub`

#### Scenario: Mode and model resolution
- **WHEN** mode/model are not specified in the request
- **THEN** mode SHALL fall back to prompt template's mode, then `Build`
- **AND** model SHALL fall back to project's `DefaultModel`, then `"sonnet"`

### Requirement: Queue orchestration for issue trees

The system SHALL expand an issue tree into series/parallel lanes and dispatch agents in topological order.

#### Scenario: Series children dispatch sequentially
- **WHEN** a parent issue has series children A→B→C
- **THEN** each child SHALL dispatch only after the previous reaches a terminal session status

#### Scenario: Parallel groups dispatch concurrently
- **WHEN** children are in parallel groups
- **THEN** items within a group SHALL run concurrently
- **AND** the next group SHALL start only after all items in the previous group complete

#### Scenario: Failed item stops its lane
- **WHEN** any queued item fails agent startup
- **THEN** the owning TaskQueue SHALL mark it Failed
- **AND** remaining pending items in that lane SHALL NOT be processed
- **AND** sibling lanes SHALL continue unaffected

#### Scenario: Cancel drains pending items
- **WHEN** `POST /queue/cancel` is called
- **THEN** every pending item SHALL be marked cancelled
- **AND** already-running items SHALL continue to completion

### Requirement: Base-branch resolution with blocking checks

The system SHALL resolve base branches with blocking checks for open children and open prior series siblings.

#### Scenario: Open child issues block dispatch
- **WHEN** the issue has open child issues
- **THEN** the resolver SHALL return `Blocked = true` with a message naming the blocking children

#### Scenario: Open prior series siblings block dispatch
- **WHEN** the issue has open prior series siblings
- **THEN** the resolver SHALL return `Blocked = true` naming the prior siblings

#### Scenario: Explicit base branch wins
- **WHEN** `BaseBranch` is supplied in the request
- **THEN** the explicit branch SHALL override any inferred value

#### Scenario: Prior sibling PR branch auto-detected
- **WHEN** no explicit override and a completed prior sibling has an open PR
- **THEN** the resolver SHALL return that sibling's PR branch for stacked-PR support

### Requirement: AI branch-id generation

The system SHALL generate meaningful kebab-case branch ids via a sidecar, with a deterministic fallback.

#### Scenario: Sidecar generates branch id
- **WHEN** `POST /api/orchestration/generate-branch-id` is called and the sidecar is reachable
- **THEN** the result SHALL be kebab-case, ≤50 characters, derived from issue title/description

#### Scenario: Background path falls back on sidecar failure
- **WHEN** the sidecar is unreachable during background branch-id generation
- **THEN** the service SHALL fall back to a deterministic slug of the issue title

### Requirement: Active-agents visibility

The header SHALL display a count of active agents, updating live via SignalR.

#### Scenario: Zero active agents shows compact indicator
- **WHEN** zero agents are active
- **THEN** the indicator SHALL render compactly with count = 0

#### Scenario: Error accent for errored sessions
- **WHEN** any agent is in Error status
- **THEN** an error accent SHALL be applied to the indicator

#### Scenario: Count updates without page reload
- **WHEN** agent count changes
- **THEN** the badge SHALL update via TanStack Query + hub invalidation
