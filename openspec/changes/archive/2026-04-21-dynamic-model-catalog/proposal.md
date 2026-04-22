## Why

Model names are hard-coded in six places: frontend `TASK_MODELS`/`ISSUES_MODELS` arrays (`run-agent-dialog.tsx:36-46`), a stub `AvailableModels` list in `ClaudeModelInfo.cs`, and four server controllers that each fall back to `"opus"` or `"sonnet"` differently (`SessionsController:122`, `IssuesAgentController:111`, `ClonesController:164`, `IssuesController:522`). The UI can only offer the three tier aliases ("opus", "sonnet", "haiku") — there is no way to pin a specific version, surface newly released models, or show accurate display names. Capability flags on the DTO are stubbed but never consumed. The Anthropic API already exposes a canonical model list; the server should be the single source of truth so UI options, defaults, and downstream session creation all agree on one catalog.

## What Changes

- Add the official Anthropic C# SDK NuGet package to `Homespun.Server` and register an `IAnthropicClient` authenticated with `CLAUDE_CODE_OAUTH_TOKEN`.
- New `ModelCatalogService` (live) + `MockModelCatalogService` (mock-mode/tests) behind `IModelCatalogService`, registered via `MockServiceExtensions`. Live implementation wraps `IAnthropicClient.Models.ListAsync()` with `IMemoryCache` (24h absolute TTL). Failures return the hardcoded fallback list and are not cached.
- Default selection rule: preference `[opus, sonnet, haiku]`; within each tier, pick the model whose id contains the tier name with the largest `created_at`; fall through to the next tier on miss; final fallback is `models[0]`.
- New endpoint `GET /api/models` returning `ClaudeModelInfo[]` with `IsDefault=true` set on exactly one entry (server-computed).
- Reshape `ClaudeModelInfo` DTO: keep `Id`, `DisplayName`, `CreatedAt`, `IsDefault`; **remove** `SupportsThinking`, `SupportsToolUse`, `SupportsVision` (unused, not in API).
- Full API model IDs go on the wire from new selections (e.g., `claude-opus-4-7-20251101`). Legacy short-alias values in `Project.DefaultModel` and request bodies are resolved on read via a new `ResolveModelIdAsync(string?)` helper; no DB migration.
- Frontend: new `useAvailableModels` TanStack Query hook (`staleTime: 24h`). `run-agent-dialog.tsx` replaces both hardcoded arrays; default selection reads `IsDefault` from the server. localStorage values not present in the current catalog are normalized through the same tier-prefix resolution before use; resolved full IDs are persisted on next selection.
- Update the four fallback controllers to call `ResolveModelIdAsync` instead of `?? "opus"` / `?? "sonnet"`.
- Tests: unit tests for cache lifecycle, default-selection ordering, SDK-failure fallback; API test for `GET /api/models`; unit + hook tests for the frontend replacement; explicit assertions that `MockModelCatalogService` never constructs an `IAnthropicClient`.

### Out of scope

- DB migration for existing `Project.DefaultModel` values — kept as-is, resolved on read.
- Per-project tier preferences — hardcoded order for now.
- Model capability metadata — dropped; re-add at the specific consumer if and when needed.
- Background refresh / proactive cache warming — lazy-on-miss only.
- Worker-side validation of model IDs — worker continues to pass strings through to the SDK.

## Capabilities

### New Capabilities

- `model-catalog`: Server-side catalog of Anthropic models with caching, default selection, legacy resolution, mock-mode swap, and a single HTTP surface (`GET /api/models`).

### Modified Capabilities

<!-- None. The existing `claude-agent-sessions` spec only mentions "model" incidentally in session-metadata scenarios; catalog sourcing is new behavior not previously specified. -->

## Impact

- **Server**: `Features/ClaudeCode/Services/ModelCatalogService.cs` + `MockModelCatalogService.cs`, `Controllers/ModelsController.cs`, DI wiring in `MockServiceExtensions`, four controller updates to use `ResolveModelIdAsync`.
- **Shared**: `Models/Sessions/ClaudeModelInfo.cs` DTO reshape + `FallbackModels` constant (replaces today's stub `AvailableModels`).
- **Frontend**: `features/agents/hooks/use-available-models.ts`, `run-agent-dialog.tsx` (remove `TASK_MODELS`/`ISSUES_MODELS`, wire hook), OpenAPI regen.
- **Dependencies**: one new NuGet package (official Anthropic C# SDK) in `Homespun.Server.csproj`.
- **Tests**: ~6 new unit test files (server service + mock + controllers; web hook + dialog).
- **Non-breaking on the wire**: the worker still accepts any model string; short-alias request bodies remain valid until the frontend switches.
