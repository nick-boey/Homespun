## Why

In Docker mode today, the server and worker share a single cloned workspace per issue via bind-mounts. The server sees worker edits immediately in the local filesystem, which is cheap and fast, but it tightly couples server and worker to the same host filesystem. In ACA/ACI mode, the worker is a remote container with no shared filesystem. Rather than replicate the bind-mount model via Azure Files (high latency, shared-write hazards on the workdir, couples to a file-share topology), this change gives the ACI worker its own ephemeral git clone and makes the server read-only against a main-branch clone. Agent progress is durable via git pushes: the work branch (clean history) to origin and a tmp/ snapshot branch (crash recovery) debounced on significant events.

## What Changes

- ACI worker containers perform a fresh `git clone` into an ephemeral workdir on start. No mount of the working directory from Azure Files.
- Worker owns its branch for the lifetime of the session. Server is read-only against a separate main-branch clone (used for serving diffs, gitgraph, file tree, etc.).
- A lightweight non-agentic `WipSnapshotService` runs inside the worker. On significant events (e.g. after each Claude tool-use completion), debounced to at most once every N seconds, it pushes a snapshot of the current workdir state to `tmp/wip/<branchName>` on `origin` via git's plumbing (`write-tree` + `commit-tree` + `update-ref` + `push --force-with-lease`). This captures uncommitted edits without disturbing the worker's work branch.
- Snapshot pushes use the current user's GitHub token (same token that pushes the real branch).
- Cleanup: `tmp/wip/<branchName>` is auto-deleted when the corresponding pull request is merged or explicitly closed; an orphan sweep deletes any `tmp/wip/*` branches not linked to an active session older than a configurable TTL.
- Last-Write-Wins merge semantics for Fleece issues continue to apply (already handled by `Fleece.Core`); this change does not affect Fleece.
- Docker mode (VM) is UNCHANGED — continues bind-mounted workdir + shared workspace.
- `IssueWorkspaceService` gains an ACI variant that does not maintain a main-branch clone per issue (only the server's read-only main clone is needed).
- Documentation: instruct users to add `branches-ignore: ['tmp/**']` to their GitHub Actions workflows, and note that force-pushes to `tmp/` branches are expected.

## Capabilities

### New Capabilities
- `worker-clone-push`: ACI worker clone-and-push lifecycle, WIP snapshot branch mechanics, cleanup, and server read-only access to main.

### Modified Capabilities
<!-- None. `IssueWorkspaceService` is extended with an ACI-specific variant, not a behavior change to the existing Docker path. -->

## Impact

- **Backend**: New `AciIssueWorkspaceService` (implements `IIssueWorkspaceService`) for ACI mode. New `WipSnapshotService` running inside the worker as a background task. Server-side code for serving "live view" of worker state shifts from filesystem reads to git reads against the server's main clone (unchanged) plus optional `tmp/wip/*` reads for diff previews (nice-to-have, not in MVP).
- **Worker image**: Add a small process that listens for events (tool-use completion signals) and triggers the debounced push. Could be part of the existing Hono server or a sidecar process in the same container group (single-container ACI is simpler).
- **API surface**: No new public endpoints. Internal `WipSnapshotTrigger` signal hooks into `MessageProcessingService` → worker.
- **GitHub repo**: Each active session adds a `tmp/wip/<branch>` ref; bounded by active sessions × debounce cadence.
- **CI cost impact**: Documented — users must add `branches-ignore: ['tmp/**']` to avoid redundant workflow runs on every snapshot.
- **Docker/VM mode**: Unaffected.
- **Depends on**: `aci-agent-execution` (needs the ACI execution path in place) and transitively `multi-user-postgres` (needs per-user tokens).
