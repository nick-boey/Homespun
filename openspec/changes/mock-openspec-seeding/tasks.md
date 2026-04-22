## 1. Scaffolding and DI

- [x] 1.1 Create `src/Homespun.Server/Features/Testing/Services/OpenSpecMockSeeder.cs` with constructor signature `(ITempDataFolderService, ILogger<OpenSpecMockSeeder>)` and a single public `SeedAsync(string projectPath, IReadOnlyDictionary<string, string> branchToFleeceId, CancellationToken)` method
- [x] 1.2 Register `OpenSpecMockSeeder` as a singleton in `MockServiceExtensions.AddMockServices` next to the existing `FleeceIssueSeeder` registration
- [x] 1.3 Add a private static `Dictionary<string, string> BranchToFleeceId` constant in `MockDataSeederService` mapping each demo PR's `BranchName` to a seeded issue id (single source of truth used by both PR seeding and openspec seeding)

## 2. Main-branch openspec content

- [x] 2.1 In `OpenSpecMockSeeder.SeedAsync`, write `openspec/project.md` with a minimal placeholder describing the demo project
- [x] 2.2 Write an in-progress change at `openspec/changes/api-v2-design/` containing `proposal.md`, `design.md`, `specs/api-v2/spec.md`, `tasks.md` with three `## Phase` headings and partial checkbox completion, and `.homespun.yaml` with `fleeceId: ISSUE-006`, `createdBy: agent`
- [x] 2.3 Write a ready-to-archive change at `openspec/changes/rate-limiting/` with all `tasks.md` checkboxes ticked and a `.homespun.yaml` linking to a different seeded issue
- [x] 2.4 Write an orphan change at `openspec/changes/orphan-on-main/` with `proposal.md` and `tasks.md` but **no** `.homespun.yaml`
- [x] 2.5 Write an archived change at `openspec/changes/archive/2026-01-15-old-feature/` with its `.homespun.yaml` preserved and linked to a completed seeded issue
- [x] 2.6 Add a unit test asserting all four change directories exist with the expected sidecar presence/absence and that `tasks.md` parses to the expected phase counts via `TasksParser.Parse`

## 3. Per-branch clone materialisation

- [x] 3.1 In `MockGitCloneService.CreateCloneAsync`, when `LiveClaudeTestOptions.TestWorkingDirectory` is empty, call `Directory.CreateDirectory(clonePath)` and copy the seeded `openspec/` and `.fleece/` content from the parent project into the clone
- [x] 3.2 Apply the same change to `CreateCloneFromRemoteBranchAsync`
- [x] 3.3 After copying base content, invoke `OpenSpecMockSeeder.SeedBranchAsync(clonePath, branchName, fleeceId)` so each clone gets branch-specific OpenSpec deltas (new method on the seeder, takes the resolved fleece-id from the constant added in 1.3)
- [x] 3.4 Add a unit test on `MockGitCloneService` asserting that `CreateCloneAsync` returns a path that exists on disk, contains `openspec/`, and the branch-specific content matches the expected scenario for that branch
- [x] 3.5 Add a unit test asserting that when `LiveClaudeTestOptions.TestWorkingDirectory` is set, `CreateCloneAsync` does NOT call `Directory.CreateDirectory` for per-branch paths and continues to route to the shared workspace

## 4. Per-branch scenario fixtures

- [x] 4.1 In `OpenSpecMockSeeder.SeedBranchAsync`, for the PR branch mapped to ISSUE-006, write a single in-progress change with `.homespun.yaml` carrying the matching `fleeceId` (drives the "issue with linked change in progress" UI state)
- [x] 4.2 For the PR branch mapped to ISSUE-002 (dark mode), write **two** unlinked changes (no `.homespun.yaml`) under `openspec/changes/` to exercise the multi-orphan UI disambiguation flow
- [x] 4.3 For the PR branch mapped to a completed issue, write a change whose `.homespun.yaml` carries a `fleeceId` that does NOT match the branch's fleece-id (inherited-change scenario — scanner SHALL filter it out)
- [x] 4.4 For the PR branch mapped to ISSUE-003, write no `openspec/` directory at all (branch-with-no-change indicator path)
- [x] 4.5 Add a unit test that runs the full seeder against a temp directory and asserts each scenario surfaces correctly through `BranchStateResolverService.GetOrScanAsync` and `IssueGraphOpenSpecEnricher.ResolveForIssueAsync`

## 5. Wire `BeadsIssueId` onto seeded PRs

- [x] 5.1 Update `MockDataSeederService.SeedPullRequestsAsync` to set `BeadsIssueId` on each of the five demo PRs using the static mapping from 1.3
- [x] 5.2 Add an integration test (under `Homespun.Api.Tests`) that boots the mock app, requests `/api/graph/{projectId}/taskgraph/data`, and asserts that issues with linked changes have `OpenSpecStates[id].BranchState == WithChange` and non-empty `Phases` arrays

## 6. Hook the seeder into startup

- [x] 6.1 In `MockDataSeederService.StartAsync`, call `await _openSpecMockSeeder.SeedAsync(...)` after `SeedIssuesAsync` and **before** `InitializeGitRepositories` so the initial commit captures `openspec/` content
- [x] 6.2 Add `OpenSpecMockSeeder` to the constructor of `MockDataSeederService` and update DI registration accordingly
- [x] 6.3 Add a unit test asserting that after `MockDataSeederService.StartAsync` completes, `git ls-files` in the seeded project includes paths under `openspec/`

## 7. Verification

- [ ] 7.1 Run `dotnet test` — all suites green
- [ ] 7.2 Boot `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock`, open the demo project, and confirm via the existing `phase-rollup.tsx` badges that phases appear on at least one issue (the badges are still wired in this change; they get replaced in `phase-graph-rows`)
- [ ] 7.3 Verify in the browser that the multi-orphan scenario shows orphan-link UI, the no-change branch shows a white branch indicator, and the archived change shows the blue ✓ status symbol
- [ ] 7.4 Confirm that boot of any live profile (`dev-live`, `dev-windows`, `dev-container`) does not error out and does not create per-branch directories on disk
