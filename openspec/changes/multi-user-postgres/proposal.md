## Why

Homespun is currently single-user-per-instance: every deployment has one GitHub identity, one set of credentials, and a JSON file (`homespun-data.json`) as its sole data store. To support a shared ACA deployment (and to make VM deployments usable by more than one developer), we need a real user model, per-user credentials, and a concurrency-safe data store. This change establishes that foundation — everything downstream (ACI-based workers, clone-and-push execution) depends on having per-user identity and a shared relational store.

## What Changes

- **BREAKING**: Replace `JsonDataStore` (`homespun-data.json`) with a Postgres-backed store accessed via EF Core 10.
- **BREAKING**: Replace `secrets.env` per-project files with per-user secrets stored (encrypted) in Postgres.
- **BREAKING**: Move `session-metadata.json` and `.sessions/*.jsonl` message cache to Postgres tables.
- **BREAKING**: Introduce explicit user identity on every owned resource (projects, prompts, pull requests, secrets) — existing deployments start fresh, no data migration.
- Introduce a `User` entity with OIDC-sourced identity (Entra ID on ACA; optional OIDC / no-auth single-user shim on VM).
- Introduce project visibility: `private` (visible only to creator) or `public` (visible to all users on the instance). Configurable per project.
- Store per-user GitHub tokens and Claude Code OAuth tokens (encrypted, ASP.NET DataProtection).
- Admin users seeded via config (`Admin:Emails` / `Admin:ExternalIds`); first matching login becomes admin. Demotion requires explicit UI action, not config removal.
- Bundle a Postgres container in the VM deployment (`docker-compose.yml`); use Azure Database for PostgreSQL Flexible Server in the ACA deployment.
- Fleece issue data remains in `.fleece/` inside git (unchanged). DataProtection key persistence moves to Postgres-backed key storage to survive container restarts.
- Ships with Docker-mode agent execution unchanged — only the data plane and auth plane change in this phase.

## Capabilities

### New Capabilities
- `user-identity`: User accounts, OIDC authentication, admin seeding, per-user context on requests.
- `project-visibility`: Private vs public project scoping, access checks on project-owned resources (prompts, PRs, secrets).
- `persistent-data-store`: EF Core + Postgres as the canonical store for projects, pull requests, agent prompts, favorite models, session metadata, session messages.
- `user-credentials`: Per-user storage of GitHub tokens, Claude Code OAuth tokens, and project secrets, encrypted at rest.

### Modified Capabilities
<!-- No existing specs in openspec/specs/ — this change creates the first capabilities. -->

## Impact

- **Backend**: Every service that injects `IDataStore` today (~20 files across `Features/*`) migrates to EF Core repositories or `DbContext` access. `SecretsService` loses its file-based implementation and becomes DB-backed. `SessionMetadataStore` and `MessageCacheStore` become EF-backed.
- **Auth surface**: New `Microsoft.Identity.Web` pipeline on ACA builds; a development `no-auth` shim for VM mode that treats the single local developer as User #1.
- **API**: All controllers gain implicit user scoping via `ClaimsPrincipal`. Public endpoints for login, admin user management, and per-user credential CRUD are added.
- **Frontend**: MSAL React integration for ACA builds; login screen, current-user indicator, per-user settings page (tokens, secrets), project visibility toggle.
- **Deployment**: New Postgres service in `docker-compose.yml`; Bicep/Terraform for Azure Database for PostgreSQL Flexible Server in ACA. EF Core migrations run on startup.
- **Fresh-start deployments**: Existing single-user instances must back up and re-create projects/prompts/PRs after upgrade. Documented as a breaking change.
- **Dependencies added**: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.Identity.Web`, `@azure/msal-browser`, `@azure/msal-react`.
- **Dependencies removed**: None immediately; `JsonDataStore` kept only for test fixtures during transition.
- **Testing**: Test harness switches to Testcontainers-Postgres or an in-memory Postgres (Npgsql + ephemeral DB). Existing `MockDataStore` replaced with an in-memory `DbContext` variant.
