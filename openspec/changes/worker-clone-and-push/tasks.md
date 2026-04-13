## 1. Server-Side Workspace Refactor (ACI Mode)

- [ ] 1.1 Create `Features/ClaudeCode/Services/AciIssueWorkspaceService.cs` implementing `IIssueWorkspaceService`
- [ ] 1.2 Implement `EnsureProjectSetupAsync` — clones/pulls `<dataBaseDir>/projects/<projectName>/main` on the server (same as Docker path)
- [ ] 1.3 Implement `EnsureIssueWorkspaceAsync` — in ACI mode, returns a logical `IssueWorkspace` pointing at the main clone for server-side reads only; `sourcePath` is not populated
- [ ] 1.4 Implement `CleanupIssueWorkspaceAsync` — no-op for per-issue source (there is no server-side per-issue clone); retain cleanup for any per-issue scratch state if introduced later
- [ ] 1.5 Register `AciIssueWorkspaceService` in DI when `AgentExecution:Mode=Aci`; keep existing `IssueWorkspaceService` for Docker mode

## 2. Worker-Side Clone Bootstrap

- [ ] 2.1 Extend `src/Homespun.Worker/start.sh` (or an adjacent `clone.sh`) to perform `git clone <repoUrl> /workspace/src` on first start, using `$GITHUB_TOKEN`
- [ ] 2.2 Configure `git config user.email` and `user.name` from env vars (`GIT_AUTHOR_EMAIL`, `GIT_AUTHOR_NAME` — populated per-user from Postgres)
- [ ] 2.3 Checkout the Fleece-assigned branch, creating from default branch if needed
- [ ] 2.4 Expose `WORKDIR=/workspace/src` env var for the worker process
- [ ] 2.5 Ensure re-run tolerance (idempotent) in case the init script re-executes

## 3. WIP Snapshot Service (Inside Worker)

- [ ] 3.1 Add a `WipSnapshotService` module to `src/Homespun.Worker/src/` (TypeScript, same process as the Hono server for simplicity)
- [ ] 3.2 Expose a `trigger()` method; internally implement a 10s debouncer and a 60s idle-skip rule
- [ ] 3.3 On trigger: run the plumbing sequence:
  - `git add -A --intent-to-add`
  - `tree=$(git write-tree)`
  - If `tree` equals the last-pushed tree hash → skip
  - `parent=$(git rev-parse --quiet --verify refs/heads/tmp/wip/<branch>)` (may be empty)
  - `commit=$(git commit-tree $tree [-p $parent] -m "wip snapshot <timestamp>")`
  - `git update-ref refs/heads/tmp/wip/<branch> $commit`
  - `git push origin refs/heads/tmp/wip/<branch> --force-with-lease`
- [ ] 3.4 Hook `WipSnapshotService.trigger()` into the worker's message/tool-use completion pipeline
- [ ] 3.5 Emit a SignalR/log event on each push (for observability — "wip snapshot pushed at <sha>")
- [ ] 3.6 Handle push failures (rate limit, network) with exponential backoff; do not retry if tree hasn't changed

## 4. Cleanup Mechanics

- [ ] 4.1 Add `TmpBranchCleanupService` (server-side) with a GitHub API-based delete helper for `refs/heads/tmp/wip/<branch>`
- [ ] 4.2 Wire `TmpBranchCleanupService.Delete` into the existing PR merge detection (`GitHubSyncPollingService` or equivalent)
- [ ] 4.3 Wire `TmpBranchCleanupService.Delete` into session-end handling (when a worker is torn down via `AciAgentExecutionService.StopWorkerAsync`)
- [ ] 4.4 Add `TmpBranchOrphanSweepHostedService` that runs weekly:
  - Lists branches matching `tmp/wip/*` via GitHub API
  - For each, checks if a corresponding session is active (Postgres query)
  - Deletes branches with no active session older than `WipSnapshotTtlDays` (default 7)
- [ ] 4.5 Add configuration: `WipSnapshot:TtlDays`, `WipSnapshot:SweepIntervalHours`

## 5. DI & Configuration

- [ ] 5.1 Add `AgentExecution:Aci:WipSnapshot:IdleThresholdSeconds` (default 60), `:DebounceSeconds` (default 10)
- [ ] 5.2 Propagate these values into the worker via ACI env vars on container group creation (in `AciAgentExecutionService`)
- [ ] 5.3 Ensure `AciIssueWorkspaceService` registers only in ACI mode; verify Docker mode behavior is untouched

## 6. Tests

- [ ] 6.1 Unit tests (TypeScript, worker): debouncer coalesces rapid triggers; skips push when tree unchanged; respects idle threshold
- [ ] 6.2 Integration test (shell): initialize a local git repo, run the snapshot sequence, verify `tmp/wip/<branch>` contains the workdir state including untracked files
- [ ] 6.3 Unit tests (C#, server): `TmpBranchCleanupService.Delete` invokes the right GitHub API; orphan sweep correctly identifies stale branches
- [ ] 6.4 Integration test (C#): PR merge webhook triggers branch deletion via a mocked GitHub client
- [ ] 6.5 Manual smoke: simulate container crash mid-session; start a new session; verify `tmp/wip/<branch>` contains the mid-session state

## 7. Documentation

- [ ] 7.1 Add `docs/worker-clone-push.md` explaining the ACI-mode workflow, branch naming, cleanup, and CI implications
- [ ] 7.2 Update `README.md` with a call-out: "If you enable Homespun on this repo, add `branches-ignore: ['tmp/**']` to your GitHub Actions workflows"
- [ ] 7.3 Update `docs/multi-user.md` (or wherever shared-project semantics live) noting the known limitation: two users working the same issue in a shared project share a branch name
- [ ] 7.4 Update `docs/architecture/model/server-components-aca.likec4` if the workspace service needs a new component node

## 8. Pre-PR Verification

- [ ] 8.1 `dotnet test` green for server-side tests
- [ ] 8.2 Worker vitest suite green for WIP snapshot service tests
- [ ] 8.3 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test`
- [ ] 8.4 Manual smoke in a scratch ACA/ACI deploy: start a session, perform tool edits, verify `tmp/wip/<branch>` appears and updates; end session, verify cleanup; trigger simulated orphan, verify sweep deletes it
- [ ] 8.5 Manual smoke VM Docker mode: confirm workspace service behavior unchanged
