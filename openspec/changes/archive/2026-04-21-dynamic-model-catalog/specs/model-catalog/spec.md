## ADDED Requirements

### Requirement: Server fetches the Anthropic model catalog on demand

The server SHALL retrieve the list of available Claude models from the Anthropic API via the official C# SDK, using `CLAUDE_CODE_OAUTH_TOKEN` as the API credential. The catalog SHALL be the single source of truth consumed by the frontend via `GET /api/models` and by agent-creating controllers via an internal `IModelCatalogService`.

#### Scenario: Live fetch returns the catalog from the Anthropic API

- **WHEN** `IModelCatalogService.ListAsync` is called for the first time in a live profile
- **THEN** the service SHALL invoke the Anthropic SDK's models-list operation
- **AND** SHALL return one `ClaudeModelInfo` per model in the API response, populated with `Id` (full model id such as `claude-opus-4-7-20251101`), `DisplayName` (from `display_name`), and `CreatedAt` (from `created_at`)
- **AND** exactly one entry in the returned list SHALL have `IsDefault = true`, selected by the default-resolution rule

#### Scenario: Catalog is not fetched before it is needed

- **WHEN** the server starts in a live profile and no code has yet called `IModelCatalogService`
- **THEN** no outbound HTTP request to the Anthropic API SHALL have been made
- **AND** the server SHALL start successfully even if `CLAUDE_CODE_OAUTH_TOKEN` is absent — the error SHALL surface only when `ListAsync` is first invoked

### Requirement: Catalog is cached with a 24-hour absolute TTL

The service SHALL cache successful catalog responses in process memory for 24 hours from the time of the successful fetch. Failed fetches SHALL NOT be cached.

#### Scenario: Subsequent calls within 24 hours do not re-fetch

- **WHEN** `ListAsync` has just returned a successful response
- **AND** `ListAsync` is called again before 24 hours have elapsed
- **THEN** the service SHALL return the cached value without invoking the SDK

#### Scenario: Cache expires after 24 hours

- **WHEN** more than 24 hours have elapsed since the last successful fetch
- **AND** `ListAsync` is called again
- **THEN** the service SHALL invoke the SDK and repopulate the cache

#### Scenario: SDK failure is not cached

- **WHEN** the SDK throws or returns an error during `ListAsync`
- **THEN** the service SHALL return `ClaudeModelInfo.FallbackModels` with the default-selection rule applied
- **AND** the next call to `ListAsync` SHALL attempt a fresh SDK call (the fallback is not cached)

### Requirement: Default model selection uses preference-ordered newest-in-tier

The service SHALL mark exactly one model as `IsDefault` using the following deterministic rule: for each tier in the hardcoded order `[opus, sonnet, haiku]`, select models whose `Id` contains the tier name (case-insensitive); if any match, return the match with the largest `CreatedAt`. If no tier matches, return the first model in the catalog.

#### Scenario: Newest Opus wins when multiple Opus versions exist

- **WHEN** the catalog contains `claude-opus-4-7-20251101`, `claude-opus-4-6-20250701`, and one Sonnet model
- **THEN** `claude-opus-4-7-20251101` SHALL be marked `IsDefault`

#### Scenario: Falls through to newest Sonnet when no Opus is present

- **WHEN** the catalog contains two Sonnet versions and one Haiku version but no Opus
- **THEN** the newest Sonnet (by `CreatedAt`) SHALL be marked `IsDefault`

#### Scenario: Falls through to newest Haiku when no Opus or Sonnet is present

- **WHEN** the catalog contains only Haiku variants
- **THEN** the newest Haiku SHALL be marked `IsDefault`

#### Scenario: Returns first entry when no preferred tier matches

- **WHEN** the catalog contains no model whose id matches any preferred tier
- **THEN** the first entry in the catalog SHALL be marked `IsDefault`

### Requirement: Service resolves legacy short-alias model values on read

The service SHALL expose `ResolveModelIdAsync(string? requested, CancellationToken ct)` for callers holding a possibly-legacy model identifier. This method SHALL normalise null, short-alias, and exact-id inputs against the current catalog without mutating stored values.

#### Scenario: Null or empty requested value returns the current default id

- **WHEN** `ResolveModelIdAsync` is called with `null` or `""`
- **THEN** it SHALL return the id of the model currently marked `IsDefault`

#### Scenario: Exact id match returns the input unchanged

- **WHEN** `ResolveModelIdAsync` is called with a value that matches some `ClaudeModelInfo.Id` in the current catalog
- **THEN** it SHALL return that value unchanged

#### Scenario: Short-alias input resolves to the newest id in that tier

- **WHEN** `ResolveModelIdAsync` is called with `"opus"`, `"sonnet"`, or `"haiku"` (any case)
- **THEN** it SHALL return the id of the model in the catalog with the largest `CreatedAt` whose id contains that tier name

#### Scenario: Unknown value is passed through unchanged

- **WHEN** `ResolveModelIdAsync` is called with a non-empty value that is neither an exact catalog id nor a recognised tier alias
- **THEN** it SHALL return the input unchanged
- **AND** the error surface SHALL be deferred to the downstream SDK call

### Requirement: Server exposes `GET /api/models`

The server SHALL expose an authenticated HTTP endpoint `GET /api/models` that returns the current catalog as `ClaudeModelInfo[]` with `IsDefault` already computed.

#### Scenario: Endpoint returns the catalog

- **WHEN** an authenticated request reaches `GET /api/models`
- **THEN** the response body SHALL be a JSON array of `ClaudeModelInfo` objects
- **AND** exactly one object SHALL have `isDefault: true`

#### Scenario: Endpoint is included in OpenAPI output

- **WHEN** the OpenAPI spec is regenerated via `npm run generate:api:fetch`
- **THEN** a typed client method for `getModels` SHALL be emitted into `src/Homespun.Web/src/api/generated/`

### Requirement: Agent-creating controllers use `ResolveModelIdAsync` in place of hardcoded fallbacks

The server SHALL NOT hardcode short-alias model strings (`"opus"`, `"sonnet"`, `"haiku"`) as defaults inside any agent-creating controller. The previously hardcoded fallbacks in `SessionsController`, `IssuesAgentController`, `ClonesController`, and `IssuesController` SHALL be replaced with a call to `IModelCatalogService.ResolveModelIdAsync`, passing `request.Model ?? project.DefaultModel` (or the equivalent project-level hint).

#### Scenario: Null request model + null project default resolves to the catalog default

- **WHEN** a session-creation request arrives with no `Model` and the project has no `DefaultModel`
- **THEN** the controller SHALL call `ResolveModelIdAsync(null, ct)` and use the returned id

#### Scenario: Short-alias project default resolves to a full id

- **WHEN** a request arrives with no `Model` and the project has `DefaultModel = "opus"`
- **THEN** the controller SHALL call `ResolveModelIdAsync("opus", ct)` and pass the returned full id (e.g. `claude-opus-4-7-20251101`) to the worker
- **AND** the persisted session metadata SHALL record the resolved full id, not the short alias

### Requirement: `ClaudeModelInfo` DTO contract

The shared DTO `ClaudeModelInfo` SHALL expose exactly the fields required by the catalog endpoint. Capability flags absent from the Anthropic `models.list` response SHALL NOT be part of the DTO.

#### Scenario: DTO has the catalog fields only

- **WHEN** the `ClaudeModelInfo` type is inspected
- **THEN** it SHALL have properties: `Id` (string), `DisplayName` (string), `CreatedAt` (`DateTimeOffset`), `IsDefault` (bool)
- **AND** it SHALL NOT have `SupportsThinking`, `SupportsToolUse`, or `SupportsVision`

#### Scenario: Fallback models are available as a static list

- **WHEN** any caller reads `ClaudeModelInfo.FallbackModels`
- **THEN** it SHALL be a deterministic list containing one entry per short-alias tier (`opus`, `sonnet`, `haiku`) with plausible full ids and fixed past `CreatedAt` timestamps suitable for tests

### Requirement: Mock mode and tests never reach the Anthropic API

In mock profiles (`dev-mock`) and in all automated tests, `IModelCatalogService` SHALL be implemented by `MockModelCatalogService`, which returns `ClaudeModelInfo.FallbackModels` without constructing or invoking `IAnthropicClient`.

#### Scenario: Mock profile returns fallback catalog

- **WHEN** the server is launched with `dev-mock` and `GET /api/models` is called
- **THEN** the response SHALL be exactly the entries from `ClaudeModelInfo.FallbackModels` with the default-selection rule applied
- **AND** no HTTP request to `api.anthropic.com` SHALL have been issued

#### Scenario: Mock service does not construct the Anthropic client

- **WHEN** the test-suite DI container is wired with `IAnthropicClient` as a factory that throws on construction
- **AND** `MockModelCatalogService.ListAsync` is invoked
- **THEN** the call SHALL succeed without triggering the throwing factory

### Requirement: Frontend consumes the server catalog for model selection UI

The `run-agent-dialog` component SHALL obtain its model options from the server via `useAvailableModels` (a TanStack Query hook wrapping `GET /api/models`) and SHALL NOT hardcode model option arrays. Default selection SHALL use the entry returned with `isDefault: true`.

#### Scenario: Dialog options come from the server catalog

- **WHEN** the run-agent dialog opens
- **THEN** the Task Agent and Issues Agent selectors SHALL render one option per entry returned by `useAvailableModels`
- **AND** the option label SHALL be the `displayName` returned by the server
- **AND** the option value SHALL be the `id` returned by the server

#### Scenario: Dialog defaults to the server-chosen default

- **WHEN** the dialog opens with no prior localStorage selection
- **THEN** the selected option SHALL be the entry whose `isDefault` is `true`

#### Scenario: Stored localStorage value no longer in the catalog is normalised

- **WHEN** the dialog opens and localStorage holds a model id absent from the current catalog
- **AND** the stored value begins with or contains a recognised tier name (`opus`, `sonnet`, `haiku`)
- **THEN** the dialog SHALL select the newest catalog entry in that tier
- **AND** the resolved full id SHALL be persisted to localStorage on next user confirmation

#### Scenario: Stored localStorage value unrecognised and absent from catalog

- **WHEN** the dialog opens and localStorage holds a value that is neither a catalog id nor a tier-prefix hint
- **THEN** the dialog SHALL select the server-provided default and discard the stored value on next confirmation

### Requirement: Hook caches the catalog for 24 hours on the client

The `useAvailableModels` hook SHALL be configured with `staleTime: 86_400_000` (24 hours) so that dialog opens within a day of a successful fetch do not re-request the catalog.

#### Scenario: Repeated dialog opens within 24h hit the cache

- **WHEN** the dialog opens a second time within 24 hours of the first successful fetch
- **THEN** no additional `GET /api/models` request SHALL be issued
