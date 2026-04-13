## ADDED Requirements

### Requirement: Postgres as canonical data store

The system SHALL persist all structured application data (users, projects, pull requests, agent prompts, favorite models, session metadata, session messages) in a Postgres database accessed via EF Core 10.

#### Scenario: Server starts against a configured Postgres instance
- **WHEN** the server starts with a valid `ConnectionStrings:Homespun` pointing to a Postgres database
- **THEN** the system SHALL connect, run pending EF Core migrations, and begin serving requests

#### Scenario: Server refuses to start without a connection string
- **WHEN** the server starts without `ConnectionStrings:Homespun` set (outside of mock mode)
- **THEN** the system SHALL fail startup with a fatal configuration error

### Requirement: EF Core migrations run on startup

The system SHALL apply pending EF Core migrations before serving any request.

#### Scenario: Fresh database is migrated to current schema
- **WHEN** the server starts against an empty database
- **THEN** all migrations SHALL run to completion before the HTTP listener accepts requests

#### Scenario: Already-migrated database starts normally
- **WHEN** the server starts against a database at the current schema version
- **THEN** no migrations SHALL be applied and startup SHALL proceed without delay

### Requirement: JsonDataStore removal

The system SHALL NOT read from or write to `homespun-data.json` for application data after this change. The `JsonDataStore` class SHALL be removed from production DI registration.

#### Scenario: Legacy JSON file is ignored
- **WHEN** a `homespun-data.json` file exists on disk at server startup
- **THEN** the system SHALL NOT read it; all state comes from Postgres

### Requirement: Session metadata in Postgres

The system SHALL store the mapping of Claude Code session ids to issue/PR entities in a Postgres table, replacing `session-metadata.json`.

#### Scenario: Session metadata survives container restart
- **WHEN** a session metadata record is written
- **AND** the server container is restarted
- **THEN** a subsequent lookup by session id SHALL return the same metadata

### Requirement: Session messages in append-only table

The system SHALL store per-session agent messages in a Postgres `session_messages` table with columns `(id, session_id, seq, created_at, kind, payload jsonb)` and a uniqueness constraint on `(session_id, seq)`.

#### Scenario: Message is appended with next sequence number
- **WHEN** a new message arrives for `session_id = X`
- **THEN** the system SHALL assign `seq = (max(seq) + 1)` for that session and insert the row

#### Scenario: Messages are retrieved in order
- **WHEN** a caller reads all messages for a session
- **THEN** the system SHALL return rows ordered ascending by `seq`

#### Scenario: Duplicate sequence number is rejected
- **WHEN** two writers attempt to insert with the same `(session_id, seq)`
- **THEN** the later writer SHALL receive a uniqueness violation and retry with the next `seq`

### Requirement: VM deployment bundles Postgres

The system's VM deployment (`docker-compose.yml`) SHALL include a Postgres service with a named data volume that persists across container restarts.

#### Scenario: VM deploy includes postgres service
- **WHEN** a user runs the VM deployment script
- **THEN** the resulting compose stack SHALL include a `postgres` service with a volume mount for `/var/lib/postgresql/data`

### Requirement: ACA deployment uses Azure Database for PostgreSQL

The system's ACA deployment (IaC) SHALL provision an Azure Database for PostgreSQL Flexible Server and inject its connection string into the server Container App via secret reference.

#### Scenario: ACA IaC creates a Flexible Server instance
- **WHEN** the ACA IaC is applied
- **THEN** an `Microsoft.DBforPostgreSQL/flexibleServers` resource SHALL exist and be reachable from the ACA environment's subnet

### Requirement: DataProtection key storage in Postgres

The system SHALL store ASP.NET DataProtection keys in a Postgres `data_protection_keys` table via a custom `IXmlRepository`, replacing the filesystem-based key storage used today.

#### Scenario: Keys persist across container restarts
- **WHEN** an encrypted value is written
- **AND** the container is restarted
- **THEN** the value SHALL be successfully decryptable using keys read from Postgres

### Requirement: Integration tests use real Postgres

Integration tests in `Homespun.Api.Tests` SHALL run against a real Postgres instance provisioned via Testcontainers.

#### Scenario: API test fixture provisions a Postgres container
- **WHEN** the API test suite runs
- **THEN** the fixture SHALL start a Postgres 17 container, apply migrations, and tear it down at suite end

### Requirement: Fleece and .claude remain filesystem-based

The system SHALL NOT move `.fleece/` issue data or `~/.claude/` session state into Postgres. These remain git-tracked and filesystem-persistent respectively.

#### Scenario: Fleece CLI continues to work against .fleece/ folder
- **WHEN** a fleece issue is created
- **THEN** the system SHALL write to the project's `.fleece/` folder exactly as it does today
