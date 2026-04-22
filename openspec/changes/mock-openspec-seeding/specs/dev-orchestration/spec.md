## ADDED Requirements

### Requirement: OpenSpec mock data seeding

Every dev profile that seeds mock data SHALL also seed a representative set of OpenSpec artifacts so that the issue-graph OpenSpec integration (`openspec-integration` spec) can be developed and visually validated without a real project. Seeding SHALL include both main-branch artifacts in the project root and per-branch artifacts in materialized clone directories.

#### Scenario: Main-branch artifacts are seeded into the project root

- **WHEN** the server boots in any dev profile with `MockMode:SeedData=true`
- **THEN** the seeded project at `{temp}/homespun-mock-{guid}/projects/demo-project/` SHALL contain `openspec/project.md` and `openspec/changes/` populated with at least:
  - one in-progress change with a `tasks.md` containing partial-completion phase headings (`## Phase`)
  - one ready-to-archive change whose `tasks.md` checkboxes are all ticked
  - one orphan change directory with no `.homespun.yaml` sidecar
  - one archived change under `openspec/changes/archive/<dated>-<name>/` with its sidecar preserved
- **AND** every non-orphan change SHALL have a `.homespun.yaml` sidecar containing `fleeceId` and `createdBy`

#### Scenario: Per-branch clone trees materialise on disk

- **WHEN** `MockGitCloneService.CreateCloneAsync` or `CreateCloneFromRemoteBranchAsync` is invoked in dev-mock mode (i.e. `LiveClaudeTestOptions.TestWorkingDirectory` is empty)
- **THEN** the returned clone path SHALL be created on disk via `Directory.CreateDirectory`
- **AND** the directory SHALL contain at minimum the project's `openspec/` subtree and a `.fleece/` directory mirroring the main project so that `BranchStateResolverService` can scan it

#### Scenario: Per-branch clones cover orphan, multi-orphan, and inherited scenarios

- **WHEN** the seeder materialises clone directories for the seeded demo PRs
- **THEN** at least one clone SHALL contain a single in-progress change with a sidecar matching its branch's fleece-id
- **AND** at least one clone SHALL contain two unlinked changes (no sidecars) so the multi-orphan UI disambiguation flow can be exercised
- **AND** at least one clone SHALL contain an inherited change whose sidecar `fleeceId` does not match the branch (so the scanner filters it out)
- **AND** at least one clone SHALL contain no `openspec/` directory at all (branch-with-no-change indicator path)

#### Scenario: Live-mode test workspace bypasses materialisation

- **WHEN** `LiveClaudeTestOptions.TestWorkingDirectory` is non-empty (live-Claude profiles)
- **THEN** `MockGitCloneService` SHALL NOT create per-branch directories on disk and SHALL NOT seed branch-specific OpenSpec content
- **AND** all clone calls SHALL continue to route to the configured shared test workspace as today

#### Scenario: Seeded PRs link to seeded issues for branch resolution

- **WHEN** `MockDataSeederService.SeedPullRequestsAsync` runs
- **THEN** each seeded PR with a `BranchName` SHALL also have its `BeadsIssueId` set to the id of a seeded issue
- **AND** `IssueBranchResolverService.ResolveIssueBranchAsync` SHALL return the PR's branch name for that issue without needing a live GitHub call
