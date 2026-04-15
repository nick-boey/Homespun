## ADDED Requirements

### Requirement: Issue CRUD API surface

The system SHALL expose issue CRUD over `/api` with endpoints for list, get, create, update, and delete, scoped to projects.

#### Scenario: List issues for a project
- **WHEN** a client calls `GET /api/projects/{projectId}/issues`
- **THEN** the response SHALL contain all issues for that project from the in-memory cache

#### Scenario: Create issue with hierarchy positioning
- **WHEN** a client POSTs to `/api/issues` with a valid `CreateIssueRequest`
- **THEN** the server SHALL persist the issue via `Fleece.Core`
- **AND** SHALL broadcast `IssuesChanged(projectId, Created, issueId)` on `NotificationHub`
- **AND** SHALL return the issue with timestamps

#### Scenario: Create issue queues branch-id generation
- **WHEN** no `workingBranchId` is provided but a `title` is present
- **THEN** `IBranchIdBackgroundService.QueueBranchIdGenerationAsync` SHALL be invoked

#### Scenario: Create issue with parent positioning
- **WHEN** a `parentIssueId` is provided with optional `siblingIssueId` and `insertBefore`
- **THEN** `AddParentAsync` SHALL be invoked for hierarchy positioning

#### Scenario: Update auto-assigns current user
- **WHEN** `PUT /api/issues/{issueId}` is called and the issue has no assignee
- **AND** the request doesn't specify one and `dataStore.UserEmail` is configured
- **THEN** the server SHALL auto-assign the current user's email

### Requirement: Hierarchy management with cycle detection

The system SHALL support set-parent, remove-parent, remove-all-parents, and move-sibling operations with cycle detection.

#### Scenario: Set parent succeeds for valid relationship
- **WHEN** a client POSTs to `/api/issues/{childId}/set-parent` with a valid parent
- **THEN** the parent SHALL be set and `IssuesChanged` SHALL be broadcast

#### Scenario: Cycle detection rejects invalid relationship
- **WHEN** a set-parent would create a cycle
- **THEN** the response SHALL be `400 Bad Request` with the cycle message from `Fleece.Core`

#### Scenario: Move sibling rejects invalid conditions
- **WHEN** `move-sibling` is called on an issue with multiple parents or no parent
- **THEN** the response SHALL be `400 Bad Request`

### Requirement: Agent-run surface with atomic deduplication

The system SHALL expose `POST /api/issues/{issueId}/run` returning `202 Accepted` with atomic duplicate prevention.

#### Scenario: First agent run returns 202
- **WHEN** no active session exists for the issue
- **THEN** the branch name SHALL be resolved, the agent SHALL be queued for background startup
- **AND** the response SHALL be `202 Accepted`

#### Scenario: Duplicate agent run returns 409
- **WHEN** an active session already exists for the issue
- **THEN** the response SHALL be `409 Conflict` with `AgentAlreadyRunningResponse`

### Requirement: Issues Agent session lifecycle

The system SHALL support creating Issues Agent sessions, computing diffs, accepting/resolving conflicts, and cancelling.

#### Scenario: Create Issues Agent session
- **WHEN** `POST /api/issues-agent/session` is called
- **THEN** the server SHALL pull latest main, create a clone, start a session of type `IssueAgentModification`

#### Scenario: Get diff between main and session branches
- **WHEN** `GET /api/issues-agent/{sessionId}/diff` is called
- **THEN** the server SHALL compare `.fleece/` issues and return per-issue `IssueChangeDto` entries

#### Scenario: Accept changes with conflict resolution
- **WHEN** `POST /api/issues-agent/{sessionId}/accept` is called
- **THEN** `IFleeceChangeApplicationService.ApplyChangesAsync` SHALL run
- **AND** conflicts SHALL be surfaced via `IssueConflictDto`

#### Scenario: Cancel discards session changes
- **WHEN** `POST /api/issues-agent/{sessionId}/cancel` is called
- **THEN** the session SHALL be stopped and its clone cleaned up

### Requirement: Git-backed sync for .fleece/ files

The system SHALL support syncing `.fleece/` JSONL files with the git remote.

#### Scenario: Sync commits and pushes fleece changes
- **WHEN** `POST /api/fleece-sync/{projectId}/sync` is called
- **THEN** the server SHALL commit all `.fleece/` paths, push to the default branch, and reload the cache

#### Scenario: Pull reloads cache from disk
- **WHEN** `POST /api/fleece-sync/{projectId}/pull` succeeds
- **THEN** the cache SHALL be reloaded via `IProjectFleeceService.ReloadFromDiskAsync`

#### Scenario: Non-fleece changes are reported
- **WHEN** sync detects non-`.fleece/` working-tree changes
- **THEN** `HasNonFleeceChanges` and `NonFleeceChangedFiles` SHALL be reported without committing them

### Requirement: Undo/redo issue history

The system SHALL support undo and redo of issue mutations with a ring-buffered history capped at 100 entries.

#### Scenario: Undo reverses the most recent mutation
- **WHEN** `POST /api/projects/{projectId}/issues/history/undo` is called
- **THEN** the preceding snapshot SHALL be re-applied via `ApplyHistorySnapshotAsync`

#### Scenario: Redo re-applies the undone state
- **WHEN** `POST /api/projects/{projectId}/issues/history/redo` is called after an undo
- **THEN** the undone state SHALL be re-applied

#### Scenario: History is capped at 100 entries
- **WHEN** a new mutation exceeds `MaxHistoryEntries`
- **THEN** the oldest entry SHALL be pruned silently

### Requirement: Task graph filter query language

The filter query parsed by `filter-query-parser.ts` SHALL support structured predicates and free-text search.

#### Scenario: Filter by status, type, priority, assignee
- **WHEN** the user types `type:bug priority:1` into the toolbar filter
- **THEN** only matching issues SHALL remain in the graph

#### Scenario: Me keyword resolves to current user
- **WHEN** the filter contains `assignee:me`
- **THEN** `me` SHALL resolve to the configured `userEmail`

#### Scenario: Free-text searches title and description
- **WHEN** the filter contains plain text without a predicate
- **THEN** it SHALL match against issue `title` and `description`
