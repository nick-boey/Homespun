# Phase 6: CI/CD and Cleanup ✅ COMPLETE

## Context

With the Hono worker replacing the ASP.NET AgentWorker, the CI/CD pipelines and project structure need updating. This phase removes the old project, updates build workflows, and cleans up references.

**Dependencies:** All previous phases complete

## 6.1 CI/CD Workflow Updates

### `.github/workflows/cd.yml`

Update the `build-and-push-image` matrix:

```yaml
strategy:
  matrix:
    include:
      - name: homespun
        dockerfile: ./Dockerfile
        context: .
        suffix: ""
      - name: homespun-worker
        dockerfile: ./src/Homespun.Worker/Dockerfile
        context: ./src/Homespun.Worker    # Changed: context is the worker dir
        suffix: "-worker"
```

The worker entry changes:
- `dockerfile`: `./src/Homespun.Worker/Dockerfile` (was `./src/Homespun.AgentWorker/Dockerfile`)
- `context`: `./src/Homespun.Worker` (was `.` - the whole repo)
- No longer needs `BASE_IMAGE` build arg (standalone Dockerfile)
- No longer depends on `build-base-image` job for worker builds

The `build-base-image` job may still be needed for the main `homespun` image if it still uses `Dockerfile.base`. If the main Dockerfile is also updated to be standalone, this job can be removed.

### `.github/workflows/ci.yml`

- Add a job for building/linting the Hono worker:
  ```yaml
  worker-build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: cd src/Homespun.Worker && npm ci && npm run build && npm test
  ```
- Remove `Homespun.AgentWorker` from any .NET build/test steps (if referenced)

## 6.2 Project Cleanup

### Remove old projects
- Delete `src/Homespun.AgentWorker/` entirely

### Keep `Homespun.ClaudeAgentSdk`
- Still used by `LocalAgentExecutionService` for mini agents (branch name generation, etc.)
- May be replaced in a future phase but out of scope here

### Update `Homespun.sln`
- Remove `Homespun.AgentWorker` project reference
- No new project to add (TypeScript project isn't part of the .NET solution)

### Remove old types
- Remove `AgentEvent` hierarchy from `IAgentExecutionService.cs` (replaced by `SdkMessage` in Phase 2)
- Remove old SSE event type constants and parsing code from Docker service

## 6.3 Script Updates

### `scripts/run.sh`

Update worker image build section:

```bash
# Old:
# docker build -t homespun-worker:local --build-arg BASE_IMAGE=homespun-base:local -f src/Homespun.AgentWorker/Dockerfile .

# New:
docker build -t homespun-worker:local -f src/Homespun.Worker/Dockerfile src/Homespun.Worker/
```

The worker build no longer needs the base image as a build arg.

### `scripts/run.ps1` (if exists)
Same changes as `run.sh` but in PowerShell syntax.

### `scripts/mock.sh`
No changes needed (mock mode uses local agent execution, not containers).

## 6.4 Documentation Updates

Update `CLAUDE.md`:
- Project structure section: Replace `Homespun.AgentWorker` with `Homespun.Worker` (TypeScript)
- Running the application section: Note that the worker is now Node.js-based

## Critical Files to Modify
- `.github/workflows/cd.yml` - Update worker build matrix
- `.github/workflows/ci.yml` - Add Node.js worker build job
- `Homespun.sln` - Remove AgentWorker project
- `scripts/run.sh` - Update worker build command
- `scripts/run.ps1` - Update worker build command (if exists)
- `CLAUDE.md` - Update documentation

## Files to Delete
- `src/Homespun.AgentWorker/` (entire directory)

## Verification
1. `dotnet build` succeeds (solution without AgentWorker)
2. `dotnet test` passes (no broken references)
3. `cd src/Homespun.Worker && npm ci && npm run build && npm test` succeeds
4. `scripts/run.sh --local` builds both images and starts successfully
5. CI workflow runs clean on a PR
6. CD workflow builds and pushes both images on release
