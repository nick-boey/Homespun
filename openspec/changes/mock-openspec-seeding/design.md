## Context

The `openspec-integration` capability is fully implemented on the backend (`IssueGraphOpenSpecEnricher`, `BranchStateResolverService`, `ChangeScannerService`, `TasksParser`, `SidecarService`) and partially implemented on the frontend (branch/change indicator symbols + the soon-to-be-replaced `phase-rollup.tsx` badges). All of this code paths off real disk scans of `openspec/changes/` directories rooted at either the main project path or a clone path resolved through `IGitCloneService`.

In mock mode:
- `MockDataSeederService` writes `.fleece/issues_*.jsonl` and creates an empty project skeleton (`README.md`, `.gitignore`).
- `MockGitCloneService` is purely in-memory — `CreateCloneAsync` returns a path string like `{repoPath}-clones/{branch}` but never calls `Directory.CreateDirectory`.
- No `openspec/` content is ever written.
- `BranchStateResolverService` therefore returns empty snapshots for every issue, the graph never shows OpenSpec indicators, and there is no way to develop or test the integration UI without bringing up a real GitHub-backed project.

## Goals / Non-Goals

**Goals:**
- Seed enough realistic OpenSpec content into mock mode that every scenario in the `openspec-integration` spec is visible in the UI on first dev boot.
- Cover both main-branch and per-branch resolution paths (the `IssueGraphOpenSpecEnricher` reads from both).
- Keep the change additive: production code paths and `openspec-integration` requirements unchanged.
- Preserve the existing live-Claude-test path through `LiveClaudeTestOptions.TestWorkingDirectory` exactly as today.

**Non-Goals:**
- Replacing `MockGitCloneService` with the real `GitCloneService`. That would require local bare upstream repos (`git init --bare`, push seed commits, set as origin) and is a separate design call with its own perf/test-isolation tradeoffs.
- Synthesising OpenSpec data on the fly per request. Seed once at boot, write to disk, let the existing scanners observe it like they would any real project.
- Adding new UI behaviour. The `phase-graph-rows` follow-up change owns rendering work; this change just gives that work something to render against.
- Changing backend models, APIs, or wire formats.

## Decisions

### Seed real files on real disk, not in-memory fixtures

**Decision:** `OpenSpecMockSeeder` writes actual files under `TempDataFolderService.GetProjectPath(...)/openspec/...` and into materialised clone directories.

**Why:** The backend scanners (`ChangeScannerService.ScanBranchAsync`, `BranchStateResolverService.GetOrScanAsync`, `TasksParser.Parse`) all read from the filesystem. Plumbing a filesystem abstraction layer through them just to support mock mode would be a large refactor that benefits nothing else. Writing real files keeps the production scan paths exercised end-to-end in dev-mock and in the API integration tests.

**Alternative considered:** Introduce an `IFileSystem` abstraction and inject an in-memory filesystem in mock mode. Rejected — touches every scanner and parser, expands surface area for divergence, and provides no win over writing ~30 KB of markdown to a temp directory.

### Materialise clone directories from inside `MockGitCloneService`, not the seeder

**Decision:** `MockGitCloneService.CreateCloneAsync` and `CreateCloneFromRemoteBranchAsync` gain a `Directory.CreateDirectory(clonePath)` plus a hook that calls into the OpenSpec seeder for that branch's content. The seeder is the source of truth for *what* to write; the mock clone service is responsible for *when* directories appear.

**Why:** Two-phase ordering. The mock seeder needs PR data to know which branches to seed (branch name comes from the PR record). PRs are created in `SeedPullRequestsAsync`. Clones are conceptually created by the *user's* later actions (clicking "create branch") in real life — but in mock mode we want them present from boot for visual testing. Hooking the materialisation into the clone service lets us also exercise the runtime clone-creation path: tests that exercise `CreateClone` will get real directories, not stubs.

**Alternative considered:** Have the `OpenSpecMockSeeder` directly write into `{repoPath}-clones/{branch}/` paths bypassing the clone service. Rejected — duplicates the path-construction logic, leaves `MockGitCloneService` returning paths that don't exist on disk for every other code path that creates clones at runtime.

### Gate materialisation on `LiveClaudeTestOptions.TestWorkingDirectory`

**Decision:** When `LiveClaudeTestOptions.TestWorkingDirectory` is set, `MockGitCloneService` continues to route every clone call to that one shared directory and skips the per-branch seeding entirely.

**Why:** The live-Claude profiles (`dev-live`, `dev-windows`, `dev-container`) execute real Claude agent sessions against a single working directory. Seeding per-branch OpenSpec content into different paths in that mode would either silently overwrite each other or pollute the live workspace. The existing routing-to-shared-dir behaviour is correct for live mode; we just don't apply the new on-disk side effects there.

### `BeadsIssueId` on seeded PRs

**Decision:** `MockDataSeederService.SeedPullRequestsAsync` sets `BeadsIssueId` on each demo PR, mapping each PR to one of the seeded issues.

**Why:** `IssueBranchResolverService.ResolveIssueBranchAsync` finds an issue's branch by looking up `IDataStore.GetPullRequestsByProject(projectId)` and matching `pr.BeadsIssueId == issueId`. Without this, the resolver returns null for every issue and the per-branch enrichment path never fires. This is a one-line-per-PR fix and unblocks half the spec scenarios.

### Scenario coverage matrix lives in code, not config

**Decision:** The seeder hardcodes the scenario set (in-progress, ready-to-archive, archived, orphan, multi-orphan, inherited, no-change-branch) as static fixtures. No JSON file, no DI-injectable factory.

**Why:** This is dev-mode test data, not a configurable product feature. Hardcoding keeps the scenario set greppable, easy to extend with new fixtures, and obvious in code review. If a future scenario needs adding, edit the file — same pattern as the existing hardcoded `demoIssues` list in `MockDataSeederService.SeedIssuesAsync`.

### Mirror the project tree into clones, don't symlink

**Decision:** Each materialised clone gets its own copy of `.fleece/` and `openspec/` content (with branch-specific deltas).

**Why:** Symlinks have inconsistent behaviour across Windows / macOS / Linux file systems and would couple the seed content of all clones to the main project. A direct copy is ~30 KB per clone, runs once at boot, and matches what real `git clone` produces (full working tree).

## Risks / Trade-offs

- **Disk usage in dev-mock grows by N×30 KB** where N is the number of seeded clones (~5-7) → Mitigation: All under `TempDataFolderService`, cleaned up on app exit. Negligible.
- **Seeder must run before git init** in `MockDataSeederService.StartAsync` so the initial commit captures the seeded `openspec/` content → Mitigation: Explicit ordering in `StartAsync`; covered by a unit test that asserts `openspec/changes/` is tracked by git after `InitializeGitRepositories` runs.
- **Mock clone materialisation could surprise existing tests** that assumed `MockGitCloneService` was pure in-memory → Mitigation: Existing tests that don't read from the clone path won't notice. Tests that do read should benefit. Run the full `Homespun.Tests` and `Homespun.Api.Tests` suites locally and triage anything that flips.
- **Per-branch fleece-id mapping is fragile** (string IDs in two places) → Mitigation: Centralise the mapping as `private static readonly Dictionary<string, string>` in the seeder so PR seeding and openspec sidecar writing share one source of truth.
- **`LiveClaudeTestOptions.TestWorkingDirectory` gate must be checked everywhere** the seeder writes per-branch content → Mitigation: Single check at the top of the per-branch seeding loop; assert in tests that no per-branch directories exist when the option is set.

## Migration Plan

No migration. This is purely additive dev-mode plumbing. To deploy:

1. Land the change. Mock-mode boots will start seeding OpenSpec content automatically.
2. No production config changes required.
3. Rollback: revert the change. Mock seeding returns to the prior behaviour with no leftover state (temp folders are deleted on each shutdown).

## Open Questions

- **Should we also seed `openspec/specs/` content?** The integration tests don't currently scan main specs (only `openspec/changes/`). Skipping for now to keep scope tight; revisit if `phase-graph-rows` or a later change needs it.
- **Do we want the seeded scenarios driven by a small DSL** (e.g. an enum per scenario, the seeder generates files from templates) instead of inline file content? Inline is fine for the initial scenario set but if it grows past ~10 fixtures the templating pressure will be real. Defer until then.
