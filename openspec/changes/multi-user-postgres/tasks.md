## 1. Scaffolding & Dependencies

- [ ] 1.1 Add NuGet packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.Identity.Web`, `Microsoft.Identity.Web.UI` to `Homespun.Server.csproj`
- [ ] 1.2 Add `postgres:17` service to `docker-compose.yml` with a `postgres-data` named volume and a bootstrap env block (`POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`)
- [ ] 1.3 Wire `ConnectionStrings:Homespun` through `appsettings.json`, `.env.example`, and the `run.sh`/`run.ps1` launch scripts
- [ ] 1.4 Add `dotnet ef` tool installation step to `Dockerfile.base` for migration generation in CI
- [ ] 1.5 Create a new `Homespun.Server/Features/Data` folder for the `DbContext`, entities, and configurations

## 2. DbContext & Entity Model

- [ ] 2.1 Create `HomespunDbContext : DbContext` with `DbSet<>` properties for `Users`, `Projects`, `PullRequests`, `AgentPrompts`, `FavoriteModels`, `UserGitHubTokens`, `UserClaudeTokens`, `UserSecrets`, `SessionMetadata`, `SessionMessages`, `DataProtectionKeys`
- [ ] 2.2 Port existing `Project`, `PullRequest`, `AgentPrompt` models to EF-friendly entities under `Features/Data/Entities` (add navigation properties, remove JSON-serialization-only shapes)
- [ ] 2.3 Add new `User` entity with `Id`, `ExternalId`, `Email`, `DisplayName`, `IsAdmin`, `CreatedAt`, `LastLoginAt`; unique index on `ExternalId`
- [ ] 2.4 Add `owner_user_id` FK and `visibility` enum column to `Project` entity
- [ ] 2.5 Add `UserGitHubToken`, `UserClaudeToken`, `UserSecret` entities with encrypted `Value` columns (string, ciphertext)
- [ ] 2.6 Add `SessionMetadata` entity replacing `session-metadata.json` structure
- [ ] 2.7 Add `SessionMessage` entity with `(SessionId, Seq)` unique index, `Payload` as `jsonb` via `HasColumnType("jsonb")`
- [ ] 2.8 Add `DataProtectionKey` entity matching `IXmlRepository` contract
- [ ] 2.9 Write `IEntityTypeConfiguration` classes for each entity under `Features/Data/Configurations`
- [ ] 2.10 Generate initial EF migration `InitialSchema` and verify `dotnet ef migrations script` output for sanity
- [ ] 2.11 Register `HomespunDbContext` in `Program.cs` with `AddDbContext<HomespunDbContext>(options => options.UseNpgsql(...))` scoped

## 3. Migration Runner

- [ ] 3.1 Implement `DatabaseMigrationHostedService : IHostedService` that runs `dbContext.Database.MigrateAsync()` before other hosted services start (use `IHostLifetime` ordering or `StartupOrder=0` pattern)
- [ ] 3.2 Add startup guard: refuse to start when `ConnectionStrings:Homespun` is absent outside mock mode
- [ ] 3.3 Write integration test: fresh database → migrated schema → server serves requests

## 4. Repository / Data Access Layer

- [ ] 4.1 Create repository interfaces (`IProjectRepository`, `IPullRequestRepository`, etc.) mapping the narrow operations currently on `IDataStore`
- [ ] 4.2 Implement EF-backed repositories for each; ensure project queries filter by visibility + current user
- [ ] 4.3 Delete `JsonDataStore` from production DI registration; remove `IDataStore` singleton registration from `Program.cs`
- [ ] 4.4 Replace all injections of `IDataStore` in feature services with appropriate repository interfaces (~20 files)
- [ ] 4.5 Update `MockDataStore` → `InMemoryHomespunDbContext` fixture for mock mode and unit tests

## 5. DataProtection

- [ ] 5.1 Implement `PostgresXmlRepository : IXmlRepository` that stores key XML blobs in `data_protection_keys` table
- [ ] 5.2 Register DataProtection with `.PersistKeysToCustomStore<PostgresXmlRepository>()` and `.SetApplicationName("Homespun")`
- [ ] 5.3 Configure non-expiring keys and document rotation procedure in `docs/`
- [ ] 5.4 Remove filesystem-based key persistence from `Program.cs`
- [ ] 5.5 Write integration test: encrypt value, restart `DbContext` + service provider, decrypt successfully

## 6. User Identity & Authentication

- [ ] 6.1 Create `Features/Auth` folder with `AuthenticationMode` enum (`Entra`, `None`), options binding from `Authentication:*`
- [ ] 6.2 Implement `ICurrentUserAccessor` and its default implementation that resolves `HttpContext.User` → `User` row (caches for the request lifetime via `IHttpContextAccessor`)
- [ ] 6.3 Add Entra ID registration path: `builder.Services.AddAuthentication(...).AddMicrosoftIdentityWebApi(...)` gated on `Authentication:Mode=Entra`
- [ ] 6.4 Add no-auth shim: middleware that injects a synthetic `ClaimsPrincipal` with `external_id='local'` when `Authentication:Mode=None`
- [ ] 6.5 Add production-guard: refuse startup if `ASPNETCORE_ENVIRONMENT=Production` AND `Authentication:Mode=None` AND `Authentication:AllowNoAuthInProduction != true`
- [ ] 6.6 Implement `UserLifecycleService.ResolveOrCreateAsync(ClaimsPrincipal)` that upserts a `User` row on OIDC login
- [ ] 6.7 Decorate controllers with `[Authorize]` globally via a fallback policy; allow anonymous on `/health`, `/api/auth/*`, OIDC callback
- [ ] 6.8 Unit test: valid token → user resolved, updates `last_login_at`; missing user → created
- [ ] 6.9 Integration test: unauthenticated request → 401; authenticated → 200; no-auth mode → local user

## 7. Admin Seeding

- [ ] 7.1 Bind `Admin:Emails` and `Admin:ExternalIds` options
- [ ] 7.2 Extend `UserLifecycleService.ResolveOrCreateAsync` to promote newly-created or existing users matching any seed entry
- [ ] 7.3 Add startup guard: production + empty admin seeds → fail unless `Admin:AllowUnseededStart=true`
- [ ] 7.4 Implement `POST /api/users/{id}/admin` and `DELETE /api/users/{id}/admin` (admin-only)
- [ ] 7.5 Enforce "cannot demote last admin" in the delete endpoint
- [ ] 7.6 Integration tests: seeded email → admin on first login; seeded ExternalId → admin; demote last admin rejected with 409

## 8. Project Visibility

- [ ] 8.1 Add `ProjectVisibility` enum (`Private`, `Public`) and column on `Project` with default `Private`
- [ ] 8.2 Update `ProjectService.CreateAsync` / `CreateLocalAsync` to set `owner_user_id = currentUser.Id` and default visibility
- [ ] 8.3 Update project queries to apply `WHERE visibility = Public OR owner_user_id = current_user_id`
- [ ] 8.4 Add `PATCH /api/projects/{id}/visibility` (owner-only)
- [ ] 8.5 Apply visibility filter to PR list and agent prompt list endpoints
- [ ] 8.6 Verify secrets queries are always user-scoped (never leak across users even on public projects)
- [ ] 8.7 Unit tests: private project invisible to non-owner; public visible; non-owner cannot change visibility
- [ ] 8.8 Admin override tests: admin can read private projects owned by others but cannot edit

## 9. Per-User Credentials

- [ ] 9.1 Rebuild `SecretsService` to read/write from `UserSecret` table instead of `secrets.env`
- [ ] 9.2 Add `UserGitHubTokenService` with encrypt/decrypt via `IDataProtector`
- [ ] 9.3 Add `UserClaudeTokenService` mirroring GitHub token pattern
- [ ] 9.4 Implement controllers:
  - `GET/POST/DELETE /api/users/me/github-token`
  - `GET/POST/DELETE /api/users/me/claude-token`
  - `GET/PUT/DELETE /api/projects/{id}/secrets/{name}`
  - `GET /api/projects/{id}/secrets` (list names, user-scoped)
- [ ] 9.5 Update `DockerAgentExecutionService` to fetch GitHub + Claude tokens and secrets from the current user's records when launching a worker; remove fallback to env-var tokens in production paths
- [ ] 9.6 Update `GitHubClientWrapper` / `GitHubService` to take the caller's token from `ICurrentUserAccessor` rather than `GITHUB_TOKEN` env
- [ ] 9.7 Unit tests: encrypted at rest; masked on GET; deletion clears access
- [ ] 9.8 Integration test: user A's secret not visible to user B even on a public project

## 10. Session Metadata & A2A Event Store

> Depends on `a2a-native-messaging` — the `A2AEventRecord` / `SessionEventEnvelope` shape and the `A2AEventStore` interface are introduced there.

- [ ] 10.1 Rebuild `SessionMetadataStore` as an EF-backed repository over `SessionMetadata` entity
- [ ] 10.2 Rebuild `A2AEventStore` as an EF-backed repository over the `session_events` entity (see design D6); `seq` allocation via transactional `max(seq)+1` keyed by `session_id`, with retry on `(session_id, seq)` unique-violation
- [ ] 10.3 Preserve the append-before-broadcast invariant from `a2a-native-messaging`: the EF append SHALL commit before the ingestor issues the SignalR broadcast
- [ ] 10.4 Preserve `event_id` uniqueness so client-side dedup continues to work across reconnect
- [ ] 10.5 Remove JSONL filesystem writer from production DI; keep an in-memory fixture for tests
- [ ] 10.6 Integration test: survive container restart; `(session_id, seq)` uniqueness under concurrent writes; events retrieved ordered by `seq`; `event_id` stable across live + replay paths

## 11. ACA Deployment IaC

- [ ] 11.1 Add `Microsoft.DBforPostgreSQL/flexibleServers` resource to the ACA Bicep/Terraform (VNet-integrated, private DNS)
- [ ] 11.2 Store admin password in Key Vault; wire connection string as a Container App secret
- [ ] 11.3 Update `aca.likec4` model with the Postgres node and its relationships
- [ ] 11.4 Document provisioning flow in `docs/AZURE_DEPLOYMENT.md`

## 12. Frontend (React)

- [ ] 12.1 Install `@azure/msal-browser` and `@azure/msal-react`
- [ ] 12.2 Wrap the app in `MsalProvider` when `VITE_AUTH_MODE=entra`; provide a no-auth stub provider otherwise
- [ ] 12.3 Build `/login` route + unauthenticated redirect
- [ ] 12.4 Add `CurrentUser` indicator in the header with sign-out
- [ ] 12.5 Add Settings page sections: GitHub token, Claude Code token, per-project secrets
- [ ] 12.6 Add project visibility toggle on the project detail page (owner-only)
- [ ] 12.7 Add `/admin/users` page (admin-only) with list + promote/demote actions
- [ ] 12.8 Regenerate OpenAPI client (`npm run generate:api:fetch`) after backend routes land
- [ ] 12.9 E2E test (Playwright): login flow in Entra mode via mock IdP; no-auth mode skips login

## 13. Testing Infrastructure

- [ ] 13.1 Add Testcontainers.PostgreSql to `Homespun.Api.Tests` fixture
- [ ] 13.2 Replace all uses of `HomespunWebApplicationFactory`'s in-memory data store with a fixture-scoped Postgres container
- [ ] 13.3 Add a SQLite-in-memory fixture for unit tests that don't need Postgres-specific features
- [ ] 13.4 Update `tests/Homespun.Tests` to use the SQLite fixture for repository tests
- [ ] 13.5 Ensure existing tests pass against the new stack (delete/port tests that relied on JsonDataStore directly)

## 14. Documentation

- [ ] 14.1 Update `docs/multi-user.md` to reflect real multi-user support (replace single-user-per-instance framing)
- [ ] 14.2 Write `docs/database.md` covering connection string setup, migrations, backup, DataProtection key recovery
- [ ] 14.3 Update `docs/AZURE_DEPLOYMENT.md` with Postgres provisioning steps
- [ ] 14.4 Update `docs/installation.md` with Postgres requirements for VM mode
- [ ] 14.5 Update `README.md` breaking-change callout and upgrade notes for existing single-user installations (manual re-creation required)
- [ ] 14.6 Add a `docs/authentication.md` covering Entra ID registration, admin seeding, and the no-auth dev shim

## 15. Pre-PR Verification

- [ ] 15.1 `dotnet test` — all backend and API tests green
- [ ] 15.2 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test && npm run test:e2e`
- [ ] 15.3 Manual smoke: fresh VM deploy, create admin, create private + public project, set secrets, launch an agent session (Docker mode still works)
- [ ] 15.4 Manual smoke: restart Homespun container — encrypted values still decryptable, sessions resumable from DB
