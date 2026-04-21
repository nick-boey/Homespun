## Context

Three hardcoded-model touchpoints converge here:

- **Frontend** hardcodes `TASK_MODELS` and `ISSUES_MODELS` in `run-agent-dialog.tsx` (lines 36-46). Each array has three entries (opus, sonnet, haiku) with lowercase `value`s used verbatim over the wire.
- **Server** has a stub `ClaudeModelInfo.AvailableModels` list that no code reads, plus four agent-creating controllers that each apply their own fallback: `SessionsController` and two others fall back to `"sonnet"`, `IssuesAgentController` falls back to `"opus"`.
- **Worker** passes the model string straight into the Claude Agent SDK — it is the only component today that speaks to Anthropic directly (via the Node.js SDK, not the C# SDK).

Nothing on the server currently calls the Anthropic REST API from C#; the C# SDK has never been referenced in-tree. `CLAUDE_CODE_OAUTH_TOKEN` is read by `DockerAgentExecutionService` only to inject it into worker containers — the server is a pass-through. Adding live-catalog fetching makes the server the first in-tree C# caller of the Anthropic API.

The user has decided:
- **Option A** (full API IDs on the wire, not short aliases).
- **Preference-ordered newest-in-tier** for the default.
- **24h in-memory TTL** for the catalog.
- **Resolve-on-read** for legacy `Project.DefaultModel` values (no DB migration).
- **Drop** the unused capability flags on `ClaudeModelInfo`.
- **Server-computed** `IsDefault` flag in the DTO.

## Goals / Non-Goals

**Goals**

- Single source of truth for the model list (server) — UI, controllers, and session creation all read the same catalog.
- Default selection = newest version within the highest-preference tier that has at least one match.
- Existing `Project.DefaultModel = "opus"` / `"sonnet"` values and existing localStorage picks continue to work without a data migration.
- Mock mode and tests never call the Anthropic API.
- Clean failure mode: API down → UI still gets three sensible options.

**Non-Goals**

- DB migration for `Project.DefaultModel`. Resolve on read.
- localStorage rewrite on first load. Normalize on next use.
- Per-project tier preferences.
- Model capability metadata (`SupportsThinking`/`SupportsToolUse`/`SupportsVision`).
- Proactive cache refresh / background warmers.
- Worker-side model validation.
- Pagination handling (the Anthropic `models.list` endpoint returns all models in one page today; add pagination if and when the SDK signals it).

## Decisions

### D1. Catalog source of truth on the server, fronted by a single HTTP endpoint

`IModelCatalogService` owns the fetch + cache + default-selection logic. Controllers (new `ModelsController`, plus the four agent-creating ones) depend on the interface. The frontend reads via `GET /api/models` only — it never sees the Anthropic API directly.

**Why:** the OAuth token must stay server-side. Exposing the raw Anthropic endpoint via CORS would leak the token; proxying it would duplicate cache/fallback logic. A server-owned catalog also lets us precompute `IsDefault` once per response rather than re-implementing the tier rule in TypeScript.

**SDK verification note (T001):** Official `Anthropic` NuGet package v12.16.0. Namespace `Anthropic`. Entry point `IAnthropicClient` → property `Models` of type `Anthropic.Services.IModelService`. List operation is `Task<ModelListPage> List(ModelListParams parameters, CancellationToken ct)` (not `ListAsync`). `ModelListPage.Items` is `IReadOnlyList<Anthropic.Models.Models.ModelInfo>`; `ModelInfo` exposes `ID` (string), `DisplayName` (string), `CreatedAt` (`DateTimeOffset`). Auth via `ClientOptions { ApiKey, AuthToken }`; we use `AuthToken` since `CLAUDE_CODE_OAUTH_TOKEN` is an OAuth bearer. Page exposes `HasNext()` + `Paginate(ct)` for future pagination — current catalog fits in one page, so we read `Items` directly.

**Alternatives considered:**

- *Startup-only fetch, cached for process lifetime.* Fast but stale on long-running prod hosts; doesn't survive Komodo hot-swap cycles cleanly.
- *Client-side fetch via a server proxy without caching.* Rebuilds the catalog per request — wasteful and exposes the token indirectly.

### D2. Option A: full API IDs on the wire

New selections send the full id returned by the API (e.g., `claude-opus-4-7-20251101`) as the `Model` field in request bodies and persist it to `Project.DefaultModel` for new projects. The worker continues to accept any string.

**Why:** the API is the source of truth. Pinning versions in saved metadata means historical sessions can record the exact model used. Short aliases drift over time.

**Alternatives considered:**

- *Option B: keep short aliases internally.* Smaller blast radius — no migration — but loses version pinning and keeps the drift problem.
- *Option C: hybrid.* Storage complexity for marginal benefit.

### D3. Default = preference-ordered newest-within-tier

Hardcoded order: `["opus", "sonnet", "haiku"]`. For each tier in order:

1. `matches = models.Where(m => m.Id.Contains(tier, StringComparison.OrdinalIgnoreCase))`
2. If `matches.Any()`, return `matches.MaxBy(m => m.CreatedAt)`.

If no tier matches (extremely unusual), return `models.First()`.

**Why:** mirrors current human behavior — devs reach for Opus first, then Sonnet, then Haiku. Anchoring to the newest version within the chosen tier means Anthropic releases roll forward automatically without accidentally downgrading the default tier when a new Haiku ships.

**Alternatives considered:**

- *Newest overall.* Flips the default to Haiku the moment Anthropic ships a new Haiku — surprises users.
- *Newest Opus only.* Fragile if Opus is briefly missing from the listing (retirement window).

### D4. 24-hour in-memory cache, lazy-on-miss, no negative caching

Single cache entry keyed `anthropic:models:v1`. Absolute expiration 24h from write. Only successful SDK responses are cached. On SDK failure the service returns `ClaudeModelInfo.FallbackModels` synchronously; the next request retries.

**Why:** the model list changes a few times per year. 24h is long enough to make the lazy miss effectively free and short enough that production hosts pick up new models the next day without redeploy. Not caching failures means a transient API blip doesn't pin the fallback for 24h.

**Alternatives considered:**

- *Sliding TTL.* A chatty server pins stale entries forever.
- *Negative caching for errors.* Adds complexity for no real benefit — three static entries are already the fallback.
- *Background refresher hosted service.* Over-engineered for a once-a-day read.

### D5. DI swap for mock mode (same pattern as agent execution)

`MockServiceExtensions` registers `MockModelCatalogService` when `HOMESPUN_MOCK_MODE=true` *and* `MockMode:UseLiveClaudeSessions=false`. The live profiles (`dev-live`, `dev-windows`, `dev-container`, prod) get `ModelCatalogService`.

`MockModelCatalogService` returns `ClaudeModelInfo.FallbackModels` directly and never constructs an `IAnthropicClient`. Verified by a unit test that asserts no outbound HTTP when the mock service is exercised.

**Why:** mirrors the existing convention for `IAgentExecutionService` (Docker/SingleContainer/Mock), so future contributors find one DI branch to update, not two.

### D6. Resolve-on-read for legacy short-alias values

New method on `IModelCatalogService`:

```
Task<string> ResolveModelIdAsync(string? requested, CancellationToken ct)
```

Behavior:

1. `null` or empty → return the default model id from the current catalog.
2. Exact id match against catalog → return as-is.
3. Contains a known tier name (`opus`, `sonnet`, `haiku`, case-insensitive) → apply D3's newest-within-tier rule against the catalog.
4. Unknown value → return as-is (worker/SDK will surface the error; the server does not pretend to validate).

Called in each of the four agent-creating controllers **before** the `model` value is handed to the worker. Replaces every `?? "opus"` / `?? "sonnet"` fallback.

**Why:** no DB migration. Existing `Project.DefaultModel = "opus"` values continue to resolve to a usable id. Falls through to the SDK's error surface for genuinely invalid ids rather than guessing.

### D7. Drop unused capability flags on `ClaudeModelInfo`

`SupportsThinking`, `SupportsToolUse`, `SupportsVision` are removed. The Anthropic `models.list` response doesn't populate them; no code consumes them today.

**Why:** keeping them forces either a hardcoded per-tier lookup (drifts from reality) or empty placeholders (actively misleads future consumers). If a feature ever needs "does this model support vision", it'll be added at the feature's edge, not in the shared DTO.

**Alternatives considered:**

- *Keep as nullable.* Adds noise to every consumer for no payoff until the first real use case exists.
- *Hardcode by tier.* Lies the moment Anthropic ships a non-conforming model.

### D8. Token auth via explicit constructor injection

Register `IAnthropicClient` in DI with the token read from `Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")`. Do not rely on the SDK picking up `ANTHROPIC_API_KEY` from the environment — that variable is never set server-side.

If the token is absent at startup in a live profile, surface a clear error at the first `ModelCatalogService` call rather than at boot — dev-mock doesn't need the token, and we shouldn't refuse to start.

## Risks / Trade-offs

- **R1 — Official C# SDK API surface assumed.** The exact method name on the SDK (`Models.ListAsync()`, `Models.List()`, pagination shape) is unverified pending the package landing. Mitigation: implementation task verifies the SDK surface first and adapts `ModelCatalogService` to whatever the SDK exposes.
- **R2 — Token env-var mismatch.** `CLAUDE_CODE_OAUTH_TOKEN` is confirmed to work against the Anthropic API by the user; the SDK may default to reading `ANTHROPIC_API_KEY`. Explicit constructor injection (D8) sidesteps this.
- **R3 — 24h stale window on model releases.** A newly released model isn't visible for up to 24h. Acceptable: Anthropic doesn't ship daily; dev can restart to pick up changes; ops can bounce a process to refresh.
- **R4 — `FallbackModels` rot.** The hardcoded fallback list can diverge from reality. Mitigation: short aliases (`opus`, `sonnet`, `haiku`) are stable; fallback is only triggered on API outage, where a best-effort answer beats a failure.
- **R5 — localStorage values pointing at retired ids.** After Option A rolls out, a user's stored `claude-opus-4-6-YYYY…` might disappear from the catalog after a retirement. The read-normalizer (D6 client-side mirror) routes unknown values through tier-prefix resolution and persists the corrected full id on next selection.
- **R6 — Pagination.** Today `models.list` fits in one page; if Anthropic introduces pagination, the service must loop. Left as a verify-during-implementation item.

## Migration Plan

1. Verify and add the official Anthropic C# SDK NuGet package; pin the version.
2. Register `IAnthropicClient` + `IModelCatalogService` (live) in `Program.cs`; register mock in `MockServiceExtensions`.
3. Reshape `ClaudeModelInfo` (drop capability flags; rename `AvailableModels` → `FallbackModels`).
4. Implement `ModelCatalogService` with cache + default resolution + `ResolveModelIdAsync`.
5. Add `ModelsController` exposing `GET /api/models`.
6. Replace `?? "opus"` / `?? "sonnet"` fallbacks in the four controllers with `ResolveModelIdAsync`.
7. Regenerate OpenAPI client.
8. Add `useAvailableModels` hook; wire into `run-agent-dialog.tsx` replacing both arrays.
9. Add localStorage read-time normalization helper; call at dialog open.
10. Tests across server (unit + API), web (hook + dialog), mock assertions.

No destructive data migration. Existing JSONL `Project.DefaultModel = "opus"` values persist untouched; the controller-side resolve pass converts them on each session creation.
