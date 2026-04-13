## ADDED Requirements

### Requirement: ACI workers clone fresh on start

When an ACI worker container starts, it SHALL perform a fresh `git clone` of the project repository into an ephemeral workdir inside the container. No persistent mount SHALL be used for the workdir.

#### Scenario: Worker clones on first launch
- **WHEN** an ACI worker container starts for `(userId, issueId)` with no prior workdir
- **THEN** the init sequence SHALL clone the project's repository to `/workspace/src` using the user's GitHub token
- **AND** SHALL check out the Fleece-assigned branch (creating it from the default branch if it does not exist on origin)

#### Scenario: Worker re-clones on restart
- **WHEN** an ACI worker container is restarted after a crash or stop/start cycle
- **THEN** the new container SHALL perform a fresh clone rather than reusing any prior workdir state

### Requirement: Worker owns its branch

During a session, the worker SHALL be the sole committer to the work branch. The server SHALL NOT write to the worker's branch.

#### Scenario: Server does not mutate worker branch
- **WHEN** any server-side operation reads information about the worker's branch
- **THEN** the operation SHALL use `git fetch` against a read-only main clone or call the GitHub API — it SHALL NOT perform writes to the branch

### Requirement: Server operates read-only against a main clone

In ACI mode, the server SHALL maintain only a single read-only clone per project (at `<dataBaseDir>/projects/<projectName>/main`) for serving diffs, file trees, and gitgraph. Per-issue `src/` clones SHALL NOT be created on the server side.

#### Scenario: Server creates main clone on project setup
- **WHEN** a project is first registered in ACI mode
- **THEN** the server SHALL clone the repository to `<dataBaseDir>/projects/<projectName>/main` and set the remote
- **AND** SHALL NOT create any per-issue `src/` directory

#### Scenario: Server refreshes main clone on demand
- **WHEN** the server needs an up-to-date view of main
- **THEN** the server SHALL perform `git fetch origin` (or a targeted fetch) against the main clone

### Requirement: WIP snapshot service

The worker SHALL include a background service that pushes a snapshot of the current workdir state to `tmp/wip/<workBranch>` on `origin`, debounced and event-triggered.

#### Scenario: Snapshot triggered after tool use
- **WHEN** the agent completes a Claude tool-use
- **THEN** the snapshot service SHALL receive a trigger signal

#### Scenario: Debounced to at most once per 10 seconds
- **WHEN** the snapshot service receives a trigger
- **AND** the previous push was less than 10 seconds ago
- **THEN** the service SHALL defer until the debounce window elapses and coalesce further triggers within the window

#### Scenario: Snapshot uses git plumbing
- **WHEN** the snapshot service pushes
- **THEN** it SHALL compose a commit via `write-tree` + `commit-tree` + `update-ref` on `refs/heads/tmp/wip/<workBranch>`
- **AND** SHALL push via `git push origin refs/heads/tmp/wip/<workBranch> --force-with-lease`
- **AND** SHALL NOT modify the working tree, the index, or the work branch

#### Scenario: Skip push when tree unchanged
- **WHEN** the computed tree hash equals the tree hash of the last successful snapshot push
- **THEN** the service SHALL skip the push

#### Scenario: Idle beyond threshold stops snapshots
- **WHEN** no trigger has arrived for longer than the configured idle threshold (default 60 seconds)
- **THEN** the service SHALL NOT generate periodic snapshots; only event-driven triggers resume pushes

### Requirement: Snapshot pushes use current user's GitHub token

Snapshot pushes SHALL authenticate using the same `GITHUB_TOKEN` injected into the worker for the user — no separate token.

#### Scenario: Push uses user's token
- **WHEN** the snapshot service performs a push
- **THEN** the push SHALL use the token available at `$GITHUB_TOKEN` (same token used for the real work-branch push)

### Requirement: Tmp branch cleanup on PR merge

When a pull request for the work branch is merged or closed, the system SHALL delete the corresponding `tmp/wip/<workBranch>` ref.

#### Scenario: PR merge triggers tmp branch deletion
- **WHEN** the server detects a pull request for `workBranch` has been merged (via webhook or poll)
- **THEN** the server SHALL call the GitHub API to delete `refs/heads/tmp/wip/<workBranch>`

#### Scenario: Session cleanup also triggers tmp branch deletion
- **WHEN** a session is explicitly ended and its worker container is deleted
- **THEN** the server SHALL delete `refs/heads/tmp/wip/<workBranch>` if it exists

### Requirement: Orphan tmp branch sweep

The server SHALL run a periodic sweep that deletes `tmp/wip/*` branches that have no active session and exceed a configurable age.

#### Scenario: Orphan sweep runs on a timer
- **WHEN** the orphan sweep interval elapses (default weekly)
- **THEN** the server SHALL enumerate `tmp/wip/*` branches via the GitHub API
- **AND** SHALL delete any branch older than `WipSnapshotTtlDays` (default 7) with no active session

### Requirement: Docker mode unchanged

The worker clone-and-push model SHALL apply only to ACI mode. Docker mode SHALL continue using the existing bind-mounted shared workdir.

#### Scenario: VM Docker deploy behavior preserved
- **WHEN** a VM deployment is running with `AgentExecution:Mode=Docker`
- **THEN** workspaces SHALL be created via the existing `IssueWorkspaceService` (per-issue `src/` clone bind-mounted into the worker), unchanged

### Requirement: Documentation for downstream CI

The project documentation SHALL instruct users to ignore `tmp/**` branches in their GitHub Actions workflows.

#### Scenario: README and docs include branches-ignore guidance
- **WHEN** a reader consults the documentation on GitHub Actions integration
- **THEN** the documentation SHALL include a recommendation to add `branches-ignore: ['tmp/**']` to workflows that would otherwise trigger on these pushes
