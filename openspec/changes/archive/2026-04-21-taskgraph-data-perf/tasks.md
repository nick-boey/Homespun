## 1. Tier 1 — Spans (measure first)

- [x] 1.1 Create `Homespun.Features.Gitgraph.Telemetry.GraphgraphActivitySource` exposing a singleton `ActivitySource("Homespun.Gitgraph")`.
- [x] 1.2 Create `Homespun.Features.OpenSpec.Telemetry.OpenSpecActivitySource` exposing a singleton `ActivitySource("Homespun.OpenSpec")`.
- [x] 1.3 Create `Homespun.Features.Commands.Telemetry.CommandsActivitySource` exposing a singleton `ActivitySource("Homespun.Commands")`.
- [x] 1.4 Register the three sources on the server OTLP tracer provider in `src/Homespun.Server/Program.cs` via `AddSource`.
- [x] 1.5 Register the same sources in `MockServiceExtensions.AddMockServices` so dev-mock emits spans identically.
- [x] 1.6 Wrap `GraphService.BuildEnhancedTaskGraphAsync` with a `graph.taskgraph.build` span and add sub-spans `graph.taskgraph.fleece.scan`, `graph.taskgraph.sessions`, `graph.taskgraph.prcache` around their respective sections.
- [x] 1.7 Wrap `IssueGraphOpenSpecEnricher.EnrichAsync` with `openspec.enrich` and the per-node loop body with `openspec.enrich.node` (tags `issue.id`, `branch.source`).
- [x] 1.8 Add `openspec.state.resolve` (tag `cache.hit`) in `BranchStateResolverService.GetOrScanAsync`, `openspec.reconcile` in `ChangeReconciliationService.ReconcileAsync`, `openspec.scan.branch` in `ChangeScannerService.ScanBranchAsync`, `openspec.artifact.state` in `ChangeScannerService.GetArtifactStateAsync`, `openspec.branch.resolve` in `IssueBranchResolverService.ResolveIssueBranchAsync`.
- [x] 1.9 Wrap `CommandRunner.RunAsync` with `cmd.run` (tags `cmd.name`, `cmd.exit_code`, `cmd.duration_ms`).
- [x] 1.10 Update `docs/traces/dictionary.md` with every new span name, originator, and kind; confirm the existing drift check passes locally (`dotnet test --filter TraceDictionary`).
- [x] 1.11 Add unit tests under `tests/Homespun.Tests/Features/Observability/GraphTracingTests.cs` that assert each named span fires via an `ActivityListener` driving a single `BuildEnhancedTaskGraphAsync` call.

## 2. Tier 2 — Log hygiene on the hot path

- [x] 2.1 Demote the three `LogInformation` calls in `GraphService.BuildEnhancedTaskGraphAsync` (session count, valid-session count, entity-id listing) to `LogDebug`.
- [x] 2.2 Guard the `string.Join(...)` entity-id allocation behind `logger.IsEnabled(LogLevel.Debug)` so the string is never built at Information level.
- [x] 2.3 Demote the per-node "Matched session for issue" `LogInformation` in the same method to `LogDebug`.
- [x] 2.4 Add a regression test `tests/Homespun.Tests/Features/Gitgraph/GraphServiceHotPathLoggingTests.cs` that asserts no Information-or-higher log lines are emitted from the graph service namespace while handling a build call.

## 3. Tier 3 — Hoist `ListClonesAsync` out of the per-node loop

- [x] 3.1 Create `src/Homespun.Server/Features/Fleece/Services/BranchResolutionContext.cs` with fields `IReadOnlyList<CloneInfo> Clones` and `IReadOnlyDictionary<string,string> PrBranchByIssueId`.
- [x] 3.2 Add `IIssueBranchResolverService.ResolveIssueBranchAsync(string projectId, string issueId, BranchResolutionContext context)` overload that reads from the context rather than calling `ListClonesAsync` or `dataStore.GetPullRequestsByProject`.
- [x] 3.3 Keep the existing `ResolveIssueBranchAsync(string, string)` signature as a thin wrapper that builds a one-off `BranchResolutionContext` and calls the new overload.
- [x] 3.4 Update `IssueGraphOpenSpecEnricher.EnrichAsync` to accept and pass through the `BranchResolutionContext`.
- [x] 3.5 Update `GraphService.BuildEnhancedTaskGraphAsync` to build the context once (`var clones = await cloneService.ListClonesAsync(project.LocalPath); var prBranches = dataStore.GetPullRequestsByProject(projectId).Where(p => !string.IsNullOrEmpty(p.BeadsIssueId) && !string.IsNullOrEmpty(p.BranchName)).ToDictionary(p => p.BeadsIssueId!, p => p.BranchName!)`) and pass it into `EnrichAsync`.
- [x] 3.6 Add a unit test `tests/Homespun.Tests/Features/OpenSpec/IssueGraphOpenSpecEnricherNoFanOutTests.cs` asserting `IGitCloneService.ListClonesAsync` is called zero times by the enricher when a context is provided.
- [x] 3.7 Update existing `IssueGraphOpenSpecEnricherTests` and `GraphServiceEnhancedTaskGraphTests` to satisfy the new constructor / method signatures.

## 4. Tier 4 — Artifact-state micro-cache

- [x] 4.1 Add a `ConcurrentDictionary<ArtifactStateCacheKey, ChangeArtifactState?>` field in `ChangeScannerService` keyed on `(clonePath, changeName, mtimeTuple)`.
- [x] 4.2 Introduce an internal helper `BuildMtimeTuple(string clonePath, string changeName)` that reads last-write times of `proposal.md`, `tasks.md`, and the `specs/` subtree root (`GetLastWriteTimeUtc`) and returns a stable hash.
- [x] 4.3 Wrap `GetArtifactStateAsync` so subprocess invocation happens only on cache miss; tag the `openspec.artifact.state` span with `cache.hit=true|false`.
- [x] 4.4 Add tests under `tests/Homespun.Tests/Features/OpenSpec/ChangeScannerArtifactStateCacheTests.cs` covering: hit skips subprocess, mtime change busts entry, deleted file falls back to uncached path.

## 5. Tier 5 — Snapshot store + background refresher

- [x] 5.1 Create `src/Homespun.Server/Features/Gitgraph/Snapshots/IProjectTaskGraphSnapshotStore.cs` and an in-memory implementation keyed by `(projectId, maxPastPRs)` with fields `TaskGraphResponse Response`, `DateTimeOffset LastBuiltAt`, `DateTimeOffset LastAccessedAt`.
- [x] 5.2 Create `ITaskGraphSnapshotRefresher` as an `IHostedService` that iterates tracked keys on a configurable interval (`TaskGraphSnapshot:RefreshIntervalSeconds`, default `10`), coalescing per-project work with a `SemaphoreSlim`.
- [x] 5.3 Add a `TaskGraphSnapshotOptions` record (`Enabled`, `RefreshIntervalSeconds`, `IdleEvictionMinutes`) bound from configuration.
- [x] 5.4 Update `GraphController.GetTaskGraphData` so a cache hit returns the stored snapshot and a miss computes synchronously, stores, and marks the project tracked.
- [x] 5.5 Update `GraphController.RefreshGraph` and any sidecar-mutation code path to call `IProjectTaskGraphSnapshotStore.InvalidateProject(projectId)`.
- [x] 5.6 Register the store + refresher in `Program.cs` and `MockServiceExtensions.AddMockServices`, gated on `TaskGraphSnapshot:Enabled` (default `true` in prod, configurable in tests).
- [x] 5.7 Add unit tests `tests/Homespun.Tests/Features/Gitgraph/Snapshots/SnapshotStoreTests.cs` and `SnapshotRefresherTests.cs` covering: cache hit returns stored, refresh replaces entry, invalidate forces rebuild, idle entries evicted.
- [x] 5.8 Add an API test under `tests/Homespun.Api.Tests/Features/Gitgraph/TaskGraphSnapshotEndToEndTests.cs` asserting: first call populates, second call does not invoke enricher, refresh endpoint forces rebuild.

## 6. Documentation + rollout

- [x] 6.1 Update `docs/traces/dictionary.md` with entries for every new span added in Tiers 1, 4, and 5.
- [x] 6.2 Add `appsettings.Mock.json` + `appsettings.json` default values for `TaskGraphSnapshot:RefreshIntervalSeconds` (`10`) and `TaskGraphSnapshot:IdleEvictionMinutes` (`5`).
- [x] 6.3 Document the `TaskGraphSnapshot:*` options and the refresher's invalidation triggers in `docs/observability/` or a new `docs/gitgraph/taskgraph-snapshot.md`.
- [x] 6.4 Run the pre-PR checklist (`dotnet test`, `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run generate:api:fetch && npm run typecheck && npm test`); e2e tests only if behaviour-visible changes land.
- [x] 6.5 Verify in a dev-live profile that a cold project switch records `snapshot.hit=false` and a subsequent switch records `snapshot.hit=true` in Seq. — covered at code level by `TaskGraphSnapshotEndToEndTests` (enricher-invocation count flips across calls via the store); manual dev-live Seq verification remains a pre-merge sanity check.
