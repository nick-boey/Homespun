## Why

`GET /api/graph/{projectId}/taskgraph/data` takes 20–30 s in production (500 issues, ~35 clones, ~20–30 visible nodes). The single span exported today is the ASP.NET request span, so every sub-call is invisible and the hotspot cannot be pinpointed. Investigation shows the `IssueGraphOpenSpecEnricher` calls `IGitCloneService.ListClonesAsync` once **per visible node** — each call spawns ~70 `git` subprocesses — and runs the `openspec status` subprocess per change on every cache miss. Project-switch polling hits this path repeatedly. Users consistently wait several seconds to see anything, and the slow path blocks unrelated UI work from rendering.

## What Changes

- Introduce `Homespun.Gitgraph` and `Homespun.OpenSpec` `ActivitySource`s and wrap the enricher, branch resolver, branch-state resolver, scanner, and `CommandRunner.RunAsync` in spans with cardinality-safe tags (project id, issue id, change name, `cache.hit`, branch-source).
- Hoist `IGitCloneService.ListClonesAsync` out of the per-node loop in `IssueBranchResolverService`. Introduce a `BranchResolutionContext` prepared once per request (clones list + tracked-PR dictionary) so branch resolution becomes a dictionary lookup, not a disk walk.
- Add a mtime-keyed micro-cache in `ChangeScannerService` so `openspec status --change …` is only re-invoked when the change directory actually changes.
- Add a background snapshot refresher that keeps a per-project `TaskGraphResponse` warm; the endpoint serves the snapshot synchronously and triggers refresh on a TTL or explicit invalidation (refresh endpoint, sidecar mutations, archive transitions).
- Demote the three `LogInformation` calls inside `GraphService.BuildEnhancedTaskGraphAsync` (session summary + entity-id listing) to `LogDebug`, drop the per-request `string.Join(…)` allocations on the hot path.
- Register the background refresher + snapshot store in both production `Program.cs` and the mock service graph so dev-mock behaviour matches prod shape.

## Capabilities

### New Capabilities

(None — all changes land inside the existing OpenSpec integration + observability surfaces.)

### Modified Capabilities

- `openspec-integration`: the "Issue graph change indicators" requirement changes from a synchronous per-request enrichment to a background-refreshed snapshot with a stale-while-revalidate contract; new requirement added for the artifact-state micro-cache invalidation rules.

## Impact

- **Code**:
  - `src/Homespun.Server/Features/Gitgraph/Services/GraphService.cs`
  - `src/Homespun.Server/Features/OpenSpec/Services/IssueGraphOpenSpecEnricher.cs`
  - `src/Homespun.Server/Features/OpenSpec/Services/ChangeScannerService.cs`
  - `src/Homespun.Server/Features/OpenSpec/Services/BranchStateResolverService.cs`
  - `src/Homespun.Server/Features/Fleece/Services/IssueBranchResolverService.cs`
  - `src/Homespun.Server/Features/Commands/CommandRunner.cs`
  - `src/Homespun.Server/Program.cs` (register `ActivitySource`s on the OTLP tracer provider, register snapshot refresher hosted service)
  - `src/Homespun.Server/Features/Testing/MockServiceExtensions.cs` (same registrations for dev-mock)
- **APIs**: no breaking change to `GET /api/graph/{projectId}/taskgraph/data` response shape. Existing `POST /api/graph/{projectId}/refresh` gains an additional invalidation responsibility (busts the snapshot cache, not just the PR cache).
- **Observability**: new `ActivitySource` names appear in traces and in `docs/traces/dictionary.md`; drift check in the server test suite will refuse to merge without a dictionary update.
- **Dependencies**: none. No new NuGet packages.
- **Runtime**: background hosted service adds a small steady-state CPU cost per registered project; amortised over request traffic it is a net win.
