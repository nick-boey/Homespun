# Phase 3: Issue Workspace Service + Folder Structure ✅ COMPLETE

## Context

The current architecture stores agent data centrally (`/data/sessions/`) with no per-issue isolation. Claude Code's `.claude/` directory (which contains session history for resumption) is not tied to any particular issue. This phase introduces a per-issue folder hierarchy where each Fleece issue gets its own `.claude/`, `.sessions/`, and `src/` directories, enabling isolated agent workspaces with natural session resumption.

## 3.1 New Folder Hierarchy

The main Homespun application container manages this structure:

```
/home/.claude/                                           # Main app's mini agent Claude data
/home/projects/{project-name}/                           # All data for a project
/home/projects/{project-name}/main/                      # Main branch clone (for reading Fleece issues)
/home/projects/{project-name}/issues/                    # Per-issue data root
/home/projects/{project-name}/issues/{issue-id}/
  .claude/                                               # Claude Code state for this issue
  .sessions/                                             # JSONL message cache (Homespun writes)
  src/                                                   # Git clone for agent work
```

When mounting to a worker container:
- `issues/{issue-id}/.claude/` -> `/home/homespun/.claude` (Claude Code state)
- `issues/{issue-id}/src/` -> `/workdir` (working directory)

## 3.2 `IssueWorkspaceService`

Create `src/Homespun/Features/ClaudeCode/Services/IssueWorkspaceService.cs`:

```csharp
public interface IIssueWorkspaceService
{
    /// Ensures the project's main branch clone exists and is up-to-date.
    Task EnsureProjectSetupAsync(string projectId, string projectName, string repoUrl, string defaultBranch, CancellationToken ct);

    /// Creates or verifies the per-issue folder structure (clone, .claude, .sessions).
    /// Creates and checks out the issue branch if needed.
    Task<IssueWorkspace> EnsureIssueWorkspaceAsync(string projectName, string issueId, string repoUrl, string branchName, CancellationToken ct);

    /// Gets the workspace paths for an existing issue.
    IssueWorkspace? GetIssueWorkspace(string projectName, string issueId);

    /// Cleans up the workspace for a completed/archived issue.
    Task CleanupIssueWorkspaceAsync(string projectName, string issueId, CancellationToken ct);
}

public record IssueWorkspace(
    string IssueId,
    string ClaudePath,     // /home/projects/{name}/issues/{id}/.claude/
    string SessionsPath,   // /home/projects/{name}/issues/{id}/.sessions/
    string SourcePath,     // /home/projects/{name}/issues/{id}/src/
    string BranchName
);
```

### Setup Flow

When a user starts an agent for an issue:

1. **Project setup** (if not already done):
   - Create `/home/projects/{project-name}/main/`
   - `git clone {repo-url} /home/projects/{project-name}/main/` (or `git pull` if exists)

2. **Issue setup** (if not already done):
   - `mkdir -p /home/projects/{project-name}/issues/{issue-id}/.claude/`
   - `mkdir -p /home/projects/{project-name}/issues/{issue-id}/.sessions/`
   - `git clone {repo-url} /home/projects/{project-name}/issues/{issue-id}/src/`
   - `git checkout -b {branch-name}` in the `src/` directory (or checkout existing branch)

## 3.3 Integration Points

### `ClaudeSessionService.StartSessionAsync`
Call `EnsureIssueWorkspaceAsync` before starting the agent. Pass the workspace paths into `AgentStartRequest`.

### `MessageCacheStore`
Update to write to `{issueWorkspace.SessionsPath}/{sessionId}.jsonl` instead of the central `/data/sessions/` path. The store should accept a base path parameter rather than using a global path.

### `ClaudeSessionDiscovery`
Update to scan `{issueWorkspace.ClaudePath}/projects/` for session files. This enables finding resumable sessions for a specific issue.

### `ProjectService`
The current `HomespunBasePath` (`~/.homespun/src/`) changes to `/home/projects/`. Update references accordingly.

## Critical Files to Modify
- `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs` - Integrate workspace setup
- `src/Homespun/Features/ClaudeCode/Services/MessageCacheStore.cs` - Update paths to per-issue
- `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionDiscovery.cs` - Update scan paths
- `src/Homespun/Features/Projects/ProjectService.cs` - Update base path references

## Reusable Existing Code
- `src/Homespun/Features/Git/GitWorktreeService.cs` - Has clone logic (`git clone --local`) and branch operations
- `src/Homespun/Features/Commands/ICommandRunner.cs` - Shell command execution for git operations

## Tests
- `IssueWorkspaceService`: Verify folder creation, path resolution, idempotency
- `IssueWorkspaceService`: Verify git clone + branch checkout
- `IssueWorkspaceService`: Verify cleanup removes correct directories
- `MessageCacheStore`: Verify writing to per-issue path
- `ClaudeSessionDiscovery`: Verify scanning per-issue `.claude/` directory

## Verification
1. `dotnet test` passes
2. Starting an agent for an issue creates the expected folder structure
3. Session JSONL files are written to the issue's `.sessions/` directory
4. Session discovery finds sessions from the issue's `.claude/` directory
5. Multiple issues have fully isolated workspaces
