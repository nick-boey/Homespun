## Why

The `openspec-integration` spec defines ten distinct scenarios — orphan changes, inherited changes, archived changes, ready-to-archive changes, partial completion, multi-orphan disambiguation, and more — but the mock-mode dev profiles seed zero OpenSpec artifacts. There is no way to visually develop or test the issue↔change integration without standing up a real project. This blocks the planned `phase-graph-rows` change because reviewers cannot see virtual phase rows render against realistic data.

## What Changes

- Add `OpenSpecMockSeeder` service that writes realistic `openspec/` trees (changes, sidecars, archive entries, `tasks.md` with phase headings) into the seeded mock project.
- Extend `MockGitCloneService.CreateCloneAsync` to materialize clone directories on disk (currently in-memory only) so the real `BranchStateResolverService` and `ChangeScannerService` can scan them.
- Seed per-branch clone trees with branch-specific OpenSpec content covering: in-progress change, ready-to-archive, archived, orphan-on-main, multi-orphan-on-branch, inherited change, branch-with-no-change.
- Wire `FleeceIssueId` onto the existing seeded PRs so `IssueBranchResolverService` resolves issues to the correct seeded branch clones.
- No-op the on-disk materialization when `LiveClaudeTestOptions.TestWorkingDirectory` is set, since live-Claude mode routes every clone to a single shared workspace.

## Capabilities

### New Capabilities
<!-- None — this is mock-mode test plumbing, not a new product capability. -->

### Modified Capabilities
- `dev-orchestration`: extends the existing "Temp-dir mock data isolation" guarantees with a new requirement that the seeded mock data SHALL include OpenSpec artifacts (changes, sidecars, archive entries, branch-specific clone trees) covering the scenarios defined in `openspec-integration`. The existing `openspec-integration` spec is unchanged — its requirements already define the behavior; this change just makes mock mode capable of exercising them.

## Impact

- **New code:**
  - `src/Homespun.Server/Features/Testing/Services/OpenSpecMockSeeder.cs`
  - Tests under `tests/Homespun.Tests/Features/Testing/`
- **Modified code:**
  - `src/Homespun.Server/Features/Testing/MockDataSeederService.cs` — wire new seeder into `StartAsync` between issues seeding and git init
  - `src/Homespun.Server/Features/Testing/Services/MockGitCloneService.cs` — `Directory.CreateDirectory(clonePath)` plus minimal scaffolding write in `CreateCloneAsync` and `CreateCloneFromRemoteBranchAsync`; gated on absence of `LiveClaudeTestOptions.TestWorkingDirectory`
  - `src/Homespun.Server/Features/Testing/MockDataSeederService.cs::SeedPullRequestsAsync` — set `FleeceIssueId` on demo PRs so branch resolution finds them
  - `src/Homespun.Server/Features/Testing/MockServiceExtensions.cs` — DI registration for `OpenSpecMockSeeder`
- **No production code paths touched** outside the mock services.
- **No API or wire-format changes.** Backend `BranchStateResolverService`, `ChangeScannerService`, `TasksParser`, `IssueGraphOpenSpecEnricher` are unchanged — they already work, they just had no mock data to scan.
- **Disk usage** in dev-mock grows by a handful of small markdown files per scenario (~30 KB total). All under `TempDataFolderService` and cleaned up on app exit.
- **Follow-up:** the `phase-graph-rows` change will rely on this seeded data for visual validation.
