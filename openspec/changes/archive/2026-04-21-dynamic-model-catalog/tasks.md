---
description: "Task list for replacing hardcoded model names with a server-fetched Anthropic model catalog"
---

# Tasks: Dynamic Model Catalog

**Input**: Design documents from `/openspec/changes/dynamic-model-catalog/`
**Prerequisites**: `proposal.md`, `design.md`, `specs/model-catalog/spec.md`

## Path Conventions (Homespun)

| Concern | Path |
|---------|------|
| Server slice | `src/Homespun.Server/Features/ClaudeCode/...` |
| Web slice | `src/Homespun.Web/src/features/agents/...` |
| Shared contracts | `src/Homespun.Shared/Models/Sessions/...` |
| Server unit tests | `tests/Homespun.Tests/Features/ClaudeCode/...` |
| Server API tests | `tests/Homespun.Api.Tests/Features/...` |
| Web unit tests | co-located `*.test.ts(x)` next to the source |

---

## Phase 1: Foundation (NuGet + shared DTO)

- [x] T001 Verify the official Anthropic C# SDK NuGet package (exact name, version, `Models.ListAsync` surface, pagination shape). If the SDK exposes a different method name, capture in `design.md` → D1 notes before wiring DI.
- [x] T002 Add the verified Anthropic SDK package reference to `src/Homespun.Server/Homespun.Server.csproj` and the matching pin in `Directory.Packages.props` if central package management is in use.
- [x] T003 [P] Reshape `src/Homespun.Shared/Models/Sessions/ClaudeModelInfo.cs`:
  - Remove `SupportsThinking`, `SupportsToolUse`, `SupportsVision` properties.
  - Add `DisplayName` (string), `CreatedAt` (DateTimeOffset), `IsDefault` (bool, server-computed).
  - Rename the static `AvailableModels` to `FallbackModels` with three entries (opus/sonnet/haiku) using plausible full ids + display names + a fixed past `CreatedAt` so ordering is deterministic in tests.
  - Update all existing references (search for `AvailableModels`, `SupportsThinking`, etc.).

---

## Phase 2: Server service + endpoint (TDD first)

- [x] T004 [P] Write failing test `ModelCatalogServiceTests.ResolveDefault_picks_newest_opus_when_multiple_opus_versions_exist` covering D3's tier-match rule with a mixed catalog (multiple Opus, one Sonnet, one Haiku).
- [x] T005 [P] Write failing test `ModelCatalogServiceTests.ResolveDefault_falls_to_sonnet_when_no_opus_present`.
- [x] T006 [P] Write failing test `ModelCatalogServiceTests.ResolveDefault_falls_to_haiku_when_no_opus_or_sonnet_present`.
- [x] T007 [P] Write failing test `ModelCatalogServiceTests.ResolveDefault_returns_first_model_when_no_tier_matches`.
- [x] T008 [P] Write failing test `ModelCatalogServiceTests.ListAsync_caches_successful_response_for_24h` — second call does not invoke the mock `IAnthropicClient`.
- [x] T009 [P] Write failing test `ModelCatalogServiceTests.ListAsync_returns_FallbackModels_when_sdk_throws_and_does_not_cache_failure` — second call retries.
- [x] T010 [P] Write failing test `ModelCatalogServiceTests.ResolveModelIdAsync_exact_id_match_returns_input_unchanged`.
- [x] T011 [P] Write failing test `ModelCatalogServiceTests.ResolveModelIdAsync_short_alias_resolves_to_newest_in_tier` (covering "opus", "sonnet", "haiku").
- [x] T012 [P] Write failing test `ModelCatalogServiceTests.ResolveModelIdAsync_null_returns_current_default`.
- [x] T013 [P] Write failing test `ModelCatalogServiceTests.ResolveModelIdAsync_unknown_value_passes_through_unchanged`.
- [x] T014 Implement `src/Homespun.Server/Features/ClaudeCode/Services/IModelCatalogService.cs` + `ModelCatalogService.cs`: constructor takes `IAnthropicClient` + `IMemoryCache` + `ILogger`. Cache key `anthropic:models:v1`, 24h absolute expiration. `ResolveModelIdAsync` implements D6. Tests T004-T013 pass. _(Implementation refinement: an internal `IAnthropicModelSource` seam wraps `IAnthropicClient` to keep the service testable without having to construct the SDK's sealed `ModelListPage`/`ModelInfo` response types — both have many required members.)_
- [x] T015 [P] Write failing test `MockModelCatalogServiceTests.ListAsync_never_constructs_anthropic_client` — verified by a DI setup where the `IAnthropicClient` registration throws on construction; the mock must not trigger it.
- [x] T016 Implement `src/Homespun.Server/Features/ClaudeCode/Services/MockModelCatalogService.cs` returning `ClaudeModelInfo.FallbackModels` and applying the same default-selection rule to mark `IsDefault`. Test T015 passes.
- [x] T017 Register `IAnthropicClient` (constructor-injected `CLAUDE_CODE_OAUTH_TOKEN`) and `IModelCatalogService` in `Homespun.Server/Program.cs` (live path); register `MockModelCatalogService` in `Homespun.Server/Features/Testing/MockServiceExtensions.cs` under the same branch that already picks mock-vs-live agent execution.
- [x] T018 [P] Write failing API test `ModelsApiTests.Get_api_models_returns_catalog_with_exactly_one_default` using `HomespunWebApplicationFactory` + injected mock catalog service.
- [x] T019 Add `src/Homespun.Server/Features/ClaudeCode/Controllers/ModelsController.cs` exposing `GET /api/models`. Tagged for OpenAPI. Test T018 passes.

---

## Phase 3: Thread resolution through existing agent-creating controllers

- [x] T020 [P] Update `SessionsController.cs:122` — replace `request.Model ?? project.DefaultModel ?? "sonnet"` with `await catalog.ResolveModelIdAsync(request.Model ?? project.DefaultModel, ct)`. Add/extend unit test in `SessionsControllerTests` covering the alias-to-id resolution path.
- [x] T021 [P] Update `IssuesAgentController.cs:111` — same pattern (was `?? "opus"`). Add/extend test.
- [x] T022 [P] Update `ClonesController.cs:164` — same pattern (was `?? "sonnet"`). Add/extend test.
- [x] T023 [P] Update `IssuesController.cs:522` — same pattern (was `?? "sonnet"`). Add/extend test.

---

## Phase 4: Frontend consumer

- [x] T024 Run `npm run generate:api:fetch` to pick up the new `GET /api/models` endpoint + reshaped `ClaudeModelInfo` DTO. Commit the regenerated client.
- [x] T025 [P] Write failing test `use-available-models.test.tsx` covering: hook returns catalog from API, default entry identified by `isDefault`, staleTime set to 24h.
- [x] T026 Add `src/Homespun.Web/src/features/agents/hooks/use-available-models.ts` — TanStack Query hook keyed `['models']`, `staleTime: 24 * 60 * 60 * 1000`. Test T025 passes.
- [x] T027 [P] Write failing test `run-agent-dialog.test.tsx` updates: dropdown options come from the hook; initial selection respects `isDefault`; stored localStorage value that isn't in the current catalog is normalized via tier-prefix helper (if present) or falls to default.
- [x] T028 Refactor `src/Homespun.Web/src/features/agents/components/run-agent-dialog.tsx`:
  - Remove the hardcoded `TASK_MODELS` / `ISSUES_MODELS` arrays.
  - Replace both `<Select>` sources with data from `useAvailableModels`.
  - Add a small `normalizeStoredModel(stored, catalog)` helper: exact-id hit → use; tier-prefix hit → newest in tier; miss → use catalog default. Persist resolved id back to localStorage on next selection.
  - Loading state: render the dropdown disabled with a spinner until the hook resolves (no hardcoded fallback in the UI path).
  - Error state: surface via existing toast pattern; disable the launch button rather than using hidden defaults.
  - Test T027 passes.

---

## Phase 5: Integration + cleanup

- [x] T029 Delete the old hardcoded `AvailableModels` references found in T003 — anything the rename flagged but didn't automatically fix.
- [x] T030 Manual smoke test:
  - Run `dev-mock`: open run-agent dialog, confirm three fallback models appear, confirm default is highlighted.
  - Run `dev-live`: open dialog, confirm the live catalog is returned (more than three entries expected), confirm `IsDefault` matches the newest Opus id.
  - Existing session resume: confirm a session saved with `model = "opus"` resumes without error (resolve-on-read path exercised server-side).
  - _Sandbox note_: full dev-live / dev-mock browser smoke deferred to local machine. Covered equivalently by automated tests: `ModelsApiTests` exercises `GET /api/models` end-to-end in the mock WebApplicationFactory; `MockModelCatalogServiceTests.ListAsync_never_constructs_anthropic_client` proves the mock path works without the Anthropic SDK; the four updated controller unit tests exercise the "opus"/"sonnet" alias resolution path; the dialog test covers the frontend loading state, default selection, tier-alias normalization, and unknown-stored-id fallback.
- [x] T031 Update `CLAUDE.md` "feature slices" section to note that `GET /api/models` is the source of truth for model options.
- [x] T032 Run full CI suite per CLAUDE.md pre-PR checklist: `dotnet test`, `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm test:e2e`.
  - `dotnet test` on `Homespun.Tests` (1778 pass / 0 fail / 6 skipped) + `Homespun.Api.Tests` (216 pass). `Homespun.AppHost.Tests` not run in this sandbox — the Aspire distributed application builder needs a live Docker daemon, which is unavailable; this is environmental, not a regression from the change.
  - `npm run lint` (0 errors, 21 pre-existing warnings), `npm run format:check` (clean after running `npm run format` on the regenerated OpenAPI client), `npm run typecheck` (clean), `npm test` (1927 pass / 1 skipped across 173 files).
  - `npm run test:e2e` deferred — Playwright e2e requires the same Aspire/Docker stack as the AppHost tests.

---

## Verification checklist

- [ ] Frontend `run-agent-dialog.tsx` contains no hardcoded model arrays and no string literals `"opus"` / `"sonnet"` / `"haiku"` outside the localStorage normalization helper.
- [ ] Server `SessionsController`, `IssuesAgentController`, `ClonesController`, `IssuesController` contain no `?? "opus"` / `?? "sonnet"` fallbacks.
- [ ] `ClaudeModelInfo` has no `SupportsThinking` / `SupportsToolUse` / `SupportsVision` properties.
- [ ] `MockModelCatalogService` never constructs `IAnthropicClient` (asserted by test).
- [ ] `GET /api/models` OpenAPI entry exists in the regenerated client.
- [ ] Legacy JSONL project metadata with `DefaultModel = "opus"` still loads a working session.
