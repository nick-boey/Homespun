## Context

Today Homespun persists state in three places on disk:

1. `~/.homespun/homespun-data.json` — projects, pull requests, agent prompts, favorite models, user email (via `JsonDataStore`).
2. `secrets.env` files next to each project clone (via `SecretsService`).
3. `~/.homespun/session-metadata.json` + `sessions/*.jsonl` under the data directory (via `SessionMetadataStore` and `MessageCacheStore`).

Deployments are single-user-per-instance by design: one `.env`, one `GITHUB_TOKEN`, one `CLAUDE_CODE_OAUTH_TOKEN`, no authentication. To run a shared instance (the eventual ACA target, and for multi-developer VM use), we need per-user identity and a concurrency-safe data store that survives container restarts.

This change is the foundation for two subsequent changes — `aci-agent-execution` (replaces the Docker execution path) and `worker-clone-and-push` (changes worker workdir semantics). Those are intentionally out of scope here.

## Goals / Non-Goals

**Goals:**
- All persistent state moves to Postgres (except `.fleece/` which stays in git, and `~/.claude/` which remains a filesystem store for Claude Code's native session state).
- First-class `User` entity with OIDC identity; every owned record (project, prompt, secret, token, PR) attributes an owner.
- Per-project visibility: `private` vs `public`. Simple enum — no per-user ACLs.
- Per-user credentials (GitHub PAT, Claude OAuth token, project secrets) stored encrypted with ASP.NET DataProtection.
- Admin seeding via declarative config; first login of a seeded email becomes admin.
- Same code path for VM and ACA modes, different auth providers.
- Fresh-start deployments — no migration from `homespun-data.json` is supported.

**Non-Goals:**
- Agent execution changes (Docker mode stays intact; ACI work is a separate change).
- Fleece storage changes — Fleece remains git-based.
- Worker workdir/clone changes — separate change.
- Multi-tenant or org-level separation — one Postgres database per Homespun instance.
- Rich RBAC — only `is_admin` flag + project visibility. No role tables, no per-project member lists.
- Data migration tooling from the legacy JSON store.

## Decisions

### D1: EF Core 10 + Npgsql, single `HomespunDbContext`

**Decision:** Use Entity Framework Core 10 with the Npgsql provider. One `DbContext` covering all entities (users, projects, pull requests, agent prompts, favorite models, secrets, user credentials, session metadata, session messages).

**Rationale:** The project already targets .NET 10. EF Core is the idiomatic choice; LINQ-based repositories drop in cleanly where `IDataStore` methods live today. A single context keeps transactions simple (e.g. "delete project cascades PRs and prompts"). Npgsql has mature jsonb support, which we exploit for `session_messages.payload`.

**Alternatives considered:**
- **Dapper + hand-written SQL** — lighter weight, but migration story is manual and the current codebase has no SQL infrastructure.
- **Marten (document DB on Postgres)** — good fit for append-only message cache but overkill for the relational data we have.
- **Multiple bounded contexts** — premature; we don't have the scale or module boundaries that justify it.

### D2: Authentication — Microsoft.Identity.Web on ACA, dev-mode shim on VM

**Decision:**
- ACA build: `Microsoft.Identity.Web` (Entra ID OIDC), standard JwtBearer for API, cookie auth for the SPA redirect flow.
- VM build: auth is disabled (`Authentication:Mode=None`). A startup-created "Local Developer" user record is returned for all requests. Rationale: VM mode is explicitly stated as a development-only deployment.
- Controllers use `[Authorize]` + a `CurrentUserAccessor` that resolves to the JWT subject on ACA and to the seeded local user on VM.

**Rationale:** Uniform controller code regardless of mode. The VM shim is a trivial middleware that injects a `ClaimsPrincipal` with a synthetic subject. No extra identity provider to deploy in VM mode.

**Alternatives considered:**
- **Keycloak sidecar on VM** — too much operational surface for "it's only me, on my VM." Can be added later without breaking anything.
- **Basic auth on VM** — worse UX than no-auth for a solo developer, same security posture (network-level).

### D3: Per-user secrets + credentials use ASP.NET DataProtection with Postgres-backed key storage

**Decision:** All secret-like columns (`user_github_tokens.token`, `user_claude_tokens.token`, `user_secrets.value`) are encrypted at rest via `IDataProtector`. DataProtection keys are stored in Postgres (`data_protection_keys` table) via a small custom `IXmlRepository` implementation — **not** on disk. This ensures encrypted columns are readable after a container restart.

**Rationale:** ASP.NET DataProtection is battle-tested, key rotation is automatic, and keeping keys in the same DB as the ciphertext keeps the ACA deployment stateless. Key material is still separable via the DataProtection key ring encryption (KEK via Azure Key Vault in future — flagged as a followup).

**Alternatives considered:**
- **Azure Key Vault direct** — ideal eventual state, but adds an Azure dependency to VM mode. Followup.
- **Filesystem-persisted keys** — current pattern, but breaks statelessness for ACA.
- **Column-level PG encryption (pgcrypto)** — requires key management outside the app, duplicates what DataProtection already does well.

### D4: Project visibility is a single enum on `projects.visibility`

**Decision:** `projects.visibility IN ('private', 'public')`. `owner_user_id` is always set. Queries filter with `WHERE visibility = 'public' OR owner_user_id = @currentUser`.

**Rationale:** Matches the stated requirement ("shared = visible to all users on the instance") with minimum schema complexity. Upgrading to a `project_members` ACL table later is additive — no breaking change if/when needed.

**Trade-off:** No "shared with Alice and Bob but not Charlie" mode. Accepted — out of scope.

### D5: Admin seeding via config, with explicit UI-driven demotion

**Decision:**
- `Admin:Emails` or `Admin:ExternalIds` in config lists seeded admins.
- On OIDC login, if the identifying claim matches and the user doesn't exist yet, create them with `is_admin=true`. If they already exist, promote them on next login.
- Removing an entry from the list does **not** auto-demote — this requires a UI action by another admin.
- Empty config list on a production build fails startup with a clear error unless `Admin:AllowUnseededStart=true` is set.

**Rationale:** Config is bootstrap, not runtime policy. Stops a misconfigured deploy from leaving an instance admin-less. Matches the user's explicit request for "specific admin seeding."

### D6: Session events as an append-only Postgres table

**Decision:** Persist the A2A event records defined by `a2a-native-messaging` into Postgres, preserving their envelope shape:

```sql
session_events (
  id            bigserial PRIMARY KEY,
  session_id    text NOT NULL,
  seq           bigint NOT NULL,       -- matches A2AEventRecord.Seq
  event_id      uuid NOT NULL,         -- matches A2AEventRecord.EventId (stable across live + replay)
  received_at   timestamptz NOT NULL DEFAULT now(),
  payload       jsonb NOT NULL,        -- the raw A2A event (Task | Message | StatusUpdate | ArtifactUpdate)
  UNIQUE (session_id, seq),
  UNIQUE (event_id)
)
```

Reads are `WHERE session_id = ? AND seq > ? ORDER BY seq` (matching the `/api/sessions/{id}/events?since=N` endpoint). Streaming live stays on SignalR via the server-side ingestor introduced by `a2a-native-messaging`; Postgres LISTEN/NOTIFY is not used in this phase. The on-disk `A2AEventStore` (JSONL) is rebuilt as an EF-backed repository against this table.

**Rationale:** Append-only + ordered sequence with a dedicated `event_id` column matches the `SessionEventEnvelope` replay contract the client already depends on. Keeping the full A2A payload as jsonb preserves the verbatim-storage invariant so the server-side translator can still run over replayed rows.

**Dependency:** This schema is contingent on `a2a-native-messaging` landing first. `ClaudeMessage` / `MessageCacheStore` are deleted by that change; attempting to implement D6 before it lands would fork the storage shape.

**Trade-off:** Slightly more storage vs. flat files. Negligible.

### D7: Fresh-start upgrade, documented as breaking

**Decision:** On first startup against a Postgres DB, EF Core migrations run. No import from `homespun-data.json`. Upgrade docs tell existing users to note their projects and re-create them.

**Rationale:** The data store shape is changing enough (per-user scoping, encrypted secrets) that a migration tool is comparable work to a manual re-creation for the small existing user base. Keeps scope tight.

### D8: VM deployment adds a Postgres container to `docker-compose.yml`

**Decision:** Add a `postgres:17` service with a named volume. Connection string read from `ConnectionStrings:Homespun`. Data volume persists across container restarts.

**Rationale:** One file to change; matches how Loki/Grafana are already composed. No extra operational load in VM mode.

### D9: Test harness uses Testcontainers for integration tests, in-memory SQLite for fast unit tests

**Decision:**
- `Homespun.Api.Tests` (integration): Testcontainers-Postgres, real migrations.
- `Homespun.Tests` (unit): SQLite in-memory (`Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:")`) with EF Core — fast, and sufficient for repository-level logic that isn't Postgres-specific.
- Anywhere `MockDataStore` is used today, swap for an in-memory-backed DbContext.

**Rationale:** Testcontainers catches Postgres-specific issues (jsonb, `ON CONFLICT`, enum handling) but is slow. SQLite keeps unit tests snappy. Document: any repository using Postgres-specific features (jsonb, LISTEN/NOTIFY) needs a Testcontainers integration test, not a SQLite unit test.

## Risks / Trade-offs

- **[Risk] EF Core migration churn during development** → Mitigation: Enforce "one migration per PR" in contributing guidelines; CI fails if `dotnet ef migrations add` would produce diffs. Squash migrations before final release of this change.
- **[Risk] DataProtection key rotation loses access to old encrypted columns if keys expire** → Mitigation: Configure non-expiring keys for Homespun's use case; document recovery procedure (key export/import).
- **[Risk] Public projects inadvertently expose secrets or PR data** → Mitigation: `user_secrets` always user-scoped, never project-scoped regardless of visibility. PRs remain project-scoped; a public project's PR list is visible to all users on the instance (explicitly part of "public" semantics).
- **[Risk] VM-mode no-auth shim gets accidentally used in production** → Mitigation: Refuse to start with `ASPNETCORE_ENVIRONMENT=Production` AND `Authentication:Mode=None` unless `Authentication:AllowNoAuthInProduction=true` (intentional foot-gun with an on-ramp for edge cases).
- **[Trade-off] Testcontainers in CI adds ~15s per test run** → Accepted; alternative is diverging behavior between unit and prod.
- **[Trade-off] jsonb payloads are not queryable by domain fields** → Accepted; we access by `session_id` + `seq` only.
- **[Risk] First-deploy admin-less instance** → Mitigation: D5 startup guard.
- **[Risk] Ordering of EF migrations vs. hosted services on startup** → Mitigation: Run migrations in an `IHostedService` with `StartupOrder=0`, block subsequent services until complete.

## Migration Plan

1. Add Postgres to `docker-compose.yml`; provision Azure Database for PostgreSQL Flexible Server in ACA IaC.
2. Deploy a build that runs EF migrations but still reads from the legacy JSON store (optional safety net during a canary — may skip given "fresh start" mandate).
3. Cut over: set `Storage:Mode=Postgres`, redeploy. Existing JSON files are ignored (documented).
4. Rollback: redeploy previous build; JSON files still on disk. Users re-enter anything created during the window.

## Open Questions

- Should the Claude OAuth token ever be shared across users, or is it strictly per-user? (Working assumption: strictly per-user, matching user's spec.)
- Do we want a "service account" user for automated system actions (e.g. GitHub sync polling)? (Working assumption: yes, a `system` user seeded at migration, non-admin, can't log in.)
- DataProtection KEK in Azure Key Vault — followup ticket, not blocking this change.
