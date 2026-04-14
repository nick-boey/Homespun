## Context

Current (Docker mode): `IssueWorkspaceService` creates a `main/` clone and per-issue `issues/{id}/src` clone on the host. The server and the worker share these paths via bind-mounts — server reads file trees and diffs directly from the filesystem; worker edits them in place. When the agent eventually pushes, it pushes the work branch to `origin`.

With ACI, there is no shared host filesystem. The server is in ACA, the worker is an ACI container group, and they only share a VNet. Mounting the workdir over Azure Files works but is a poor fit: Claude Code writes hundreds of tiny files (especially via edit tools), and SMB/NFS latency compounds; concurrent-writer semantics between server UI previews and worker tool edits are fraught; and rebuilds of the tree for diff views would be slow.

The alternative (this change): worker does its own clone, server doesn't need to read the worker's workdir directly. Git pushes are the source of truth. Two branches per session:

- **Work branch** — clean, agent-driven commits. What becomes the PR.
- **`tmp/wip/<branch>`** — snapshot of uncommitted workdir state, pushed by a background service for crash recovery only. Orthogonal history from the work branch; force-pushed on every update.

## Goals / Non-Goals

**Goals:**
- ACI workers clone fresh on start, work ephemerally, and push frequently enough that a container crash loses only seconds of uncommitted work.
- Server can serve the "what does the repo look like right now on this issue?" view without reading the worker's filesystem — just `git fetch` against the main clone + optionally the `tmp/wip` branch.
- Snapshot mechanism does not interfere with the agent's own commits or branch state.
- Bounded branch clutter: `tmp/wip/*` branches clean up automatically.

**Non-Goals:**
- Changing Docker mode's workdir behavior.
- Moving `.fleece/` off disk (it remains in git — Last-Write-Wins handled by `Fleece.Core`).
- Real-time preview of in-progress worker edits in the UI (nice-to-have via `tmp/wip` reads; out of MVP).
- Tracking branch pushes to a non-origin remote (user confirmed: same repo, `tmp/` prefix).

## Decisions

### D1: Ephemeral workdir inside the ACI container

**Decision:** The worker clones to `/workspace/src` (inside the container's ephemeral disk) on start. No persistent mount for the workdir.

**Rationale:** Fast local filesystem; no cross-container contention; container crash → container is disposable, restart re-clones.

**Trade-off:** Initial clone adds ~5–30s to worker start depending on repo size. Accepted — the user explicitly preferred this over continuous-restart cloning overhead (which is why we're on Container Instances reused across sessions, not Jobs).

### D2: Two branches per session — work branch and `tmp/wip/<branch>`

**Decision:**
- Work branch name comes from Fleece (issue-scoped, unique). Agent commits and pushes to it on its own cadence.
- Snapshot branch name: `tmp/wip/<workBranch>`. Independent lineage from the work branch.

**Rationale:** Keeps a clean agent-written history on the work branch. Snapshot branch can be force-pushed freely without touching PR lineage. Separate name space is easy to filter in workflow configs.

**Open concern:** If two users work on the same issue in a shared project, their Fleece branch names would collide. This is a pre-existing concern inherited from Fleece/IssueWorkspaceService (the workspace path collides too). We accept this limitation for now; if it becomes a problem, the fix is at the Fleece / branch-id layer, not here.

### D3: Snapshot mechanics — `write-tree` + `commit-tree` + `update-ref`

**Decision:** The snapshot service runs **inside the worker container** and uses git plumbing commands rather than `git add -A && git commit`:

```bash
# Compose a commit from the current workdir index without disturbing HEAD
git -C /workspace/src add -A --intent-to-add      # noop for tracked; stages untracked for hashing
tree=$(git -C /workspace/src write-tree)
parent=$(git -C /workspace/src rev-parse --quiet --verify refs/heads/tmp/wip/$BRANCH || echo "")
msg="wip snapshot $(date -u +%FT%TZ)"
if [ -n "$parent" ]; then
  commit=$(git -C /workspace/src commit-tree "$tree" -p "$parent" -m "$msg")
else
  commit=$(git -C /workspace/src commit-tree "$tree" -m "$msg")
fi
git -C /workspace/src update-ref refs/heads/tmp/wip/$BRANCH "$commit"
git -C /workspace/src push origin "refs/heads/tmp/wip/$BRANCH" --force-with-lease
```

**Rationale:** `write-tree` captures staged content including untracked files (after `add --intent-to-add`) without moving HEAD or touching the working tree. The work branch and index stay untouched. `--force-with-lease` is safe against stale refs.

**Alternatives considered:**
- `git stash` + separate worktree on `tmp/wip`: more moving parts, risk of stash/unstash sequencing bugs.
- Plain `git add -A && git commit` on the work branch: pollutes agent's PR history with "wip" commits.

### D4: Snapshot cadence — event-driven, debounced

**Decision:** The worker's message processing fires a `WipSnapshotTrigger` signal after every Claude tool-use completion AND on "session idle" (no tool use for 10s). A debouncer coalesces rapid triggers, pushing at most once every 10 seconds. If the session has been idle for >60s, no snapshot is pushed (steady state).

**Rationale:** Bounds push frequency to ~6/minute per active worker in the busy case, zero in the idle case. Matches the earlier "max every 10s + on idle" guidance.

**Trade-off:** In bursty tool-heavy sessions, the worst case loses ~10s of edits on crash. Accepted.

### D5: Auth for snapshot pushes

**Decision:** Same `GITHUB_TOKEN` env var used for the real push. Git configured with `credential.helper=store` against a `.git-credentials` file written by the init script (existing pattern) or via `GIT_ASKPASS` helper. No separate token.

**Rationale:** The user already has push rights to the repo; snapshots are just more pushes. No additional permissions needed.

### D6: Branch cleanup

**Decision:**
- On PR merge webhook (or server-side poll detecting PR merged): server calls GitHub API to delete `tmp/wip/<workBranch>`.
- On session cleanup (worker container deleted, session ended): server deletes `tmp/wip/<workBranch>` immediately.
- Orphan sweep: a server-side hosted service enumerates `tmp/wip/*` refs weekly, deletes any older than `WipSnapshotTtlDays` (default 7) with no matching active session.

**Rationale:** Keeps the branch list clean. Worst case, orphans linger up to 7 days — bounded.

### D7: Server remains read-only against main clone

**Decision:** Server's `IssueWorkspaceService` behavior in ACI mode:
- `EnsureProjectSetupAsync` creates a single `<dataBaseDir>/projects/<projectName>/main` clone on the server's filesystem (ephemeral to the server container — restored from git on server restart).
- No per-issue `src/` path on the server side.
- Any server-side operations that previously read from `issues/{id}/src/` now either `git fetch` then read from `main/` at the PR's target commit, OR fetch `tmp/wip/<branch>` for preview (nice-to-have, post-MVP).

**Rationale:** Server only needs authoritative main + git fetch for everything else. Eliminates the coupled `issues/{id}/src/` state.

**Trade-off:** "Live preview of worker edits before the agent commits" requires reading `tmp/wip/<branch>`. Defer to a followup; not in scope for this change.

### D8: Docker mode untouched

**Decision:** The existing `IssueWorkspaceService` remains registered in Docker mode. In ACI mode, a new `AciIssueWorkspaceService` is registered that drops the per-issue `src/` path and drops the clone-on-demand semantics (the worker clones itself).

**Rationale:** Avoid churning the VM deployment. Introduce the new behavior only where new containers need it.

## Risks / Trade-offs

- **[Risk] CI workflows run on `tmp/wip/*` pushes, multiplying Actions cost** → Mitigation: update `README.md` and `docs/` to require `branches-ignore: ['tmp/**']`; additionally, avoid pushing if the tree has not changed since the last snapshot.
- **[Risk] GitHub rate limit with frequent pushes** → Mitigation: 10-second debounce floor caps push rate; monitor `x-ratelimit-remaining` in push responses and back off on 403 rate-limit errors.
- **[Risk] Orphan branches accumulate if cleanup races** → Mitigation: orphan sweep in addition to event-driven deletes; TTL is a safety net.
- **[Risk] Two users on the same issue (shared project) collide on branch names** → Mitigation: documented limitation; if it becomes real, add `userId` to Fleece branch id scheme (out of scope here).
- **[Risk] Snapshot includes secrets written to workdir (e.g. credential files)** → Mitigation: workers already operate with a `.gitignore` for common secret paths; any new secret-writing pattern must add to `.gitignore`; documented contributor checklist.
- **[Trade-off] Losing live-preview of uncommitted worker state in ACI mode's UI** → Accepted for MVP; followup to read `tmp/wip/<branch>` for preview.
- **[Trade-off] Extra ~5–30s worker startup time for clone** → Accepted; user confirmed.

## Open Questions

- Should the snapshot mechanism also include `.fleece/` edits in flight, or let Fleece's own persistence cover that? **Working assumption: Fleece writes to disk synchronously on edit, so `write-tree` captures it. No special handling needed.**
- When the work branch is force-pushed (rebase onto main), do we also force-push `tmp/wip/<branch>` to match? **Working assumption: no — `tmp/wip` lineage is orthogonal. The next snapshot starts a fresh lineage if the parent ref is gone.**
- Does the server need to `git fetch` the `tmp/wip` branches proactively, or only on demand? **Working assumption: on demand only (post-MVP preview feature).**
