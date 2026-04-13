## ADDED Requirements

### Requirement: Per-user GitHub token storage

The system SHALL store each user's GitHub Personal Access Token encrypted in a `user_github_tokens` table keyed by `user_id`. No GitHub token SHALL be shared across users.

#### Scenario: User sets their GitHub token
- **WHEN** an authenticated user submits `POST /api/users/me/github-token` with a token value
- **THEN** the system SHALL encrypt the value via `IDataProtector` and upsert the row for that user
- **AND** the raw token SHALL NOT be persisted in plaintext anywhere

#### Scenario: User retrieves their own token status
- **WHEN** an authenticated user calls `GET /api/users/me/github-token`
- **THEN** the system SHALL return a status payload indicating whether a token is present, its last-updated timestamp, and a masked preview — but NOT the decrypted value

#### Scenario: GitHub operations use the current user's token
- **WHEN** a service performs a GitHub API or git-push operation on behalf of a user
- **THEN** the system SHALL decrypt that user's token and use it for authentication
- **AND** SHALL NOT fall back to any instance-wide `GITHUB_TOKEN` environment variable in production

### Requirement: Per-user Claude Code OAuth token storage

The system SHALL store each user's Claude Code OAuth token encrypted in a `user_claude_tokens` table keyed by `user_id`.

#### Scenario: User sets their Claude Code token
- **WHEN** an authenticated user submits `POST /api/users/me/claude-token` with a token value
- **THEN** the system SHALL encrypt the value via `IDataProtector` and upsert the row for that user

#### Scenario: User's token is used when launching their agent workers
- **WHEN** a worker is started for a user's session
- **THEN** the system SHALL decrypt that user's Claude Code token and provide it to the worker via environment variable

### Requirement: Per-user project secrets

The system SHALL store project secrets in a `user_secrets` table keyed by `(user_id, project_id, name)`. Values SHALL be encrypted via `IDataProtector`.

#### Scenario: User sets a secret for one of their projects
- **WHEN** an authenticated user submits `PUT /api/projects/{id}/secrets/{name}` with a value
- **THEN** the system SHALL upsert a row with `(user_id, project_id, name)` and the encrypted value

#### Scenario: Secrets are not visible to other users on a public project
- **WHEN** user A sets secret `FOO` on public project P
- **AND** user B (not A) queries secrets for P
- **THEN** the system SHALL return only user B's own secrets for P, NOT user A's

#### Scenario: Secrets injected when agent runs on behalf of user
- **WHEN** an agent worker starts for user A on project P
- **THEN** the system SHALL decrypt and inject user A's secrets for project P as environment variables
- **AND** SHALL NOT inject secrets belonging to other users

### Requirement: secrets.env file removal

The system SHALL NOT read secrets from `secrets.env` files on disk after this change.

#### Scenario: Legacy secrets.env file is ignored
- **WHEN** a `secrets.env` file exists in a project's folder on disk
- **THEN** the system SHALL NOT read it when resolving secrets for an agent worker

### Requirement: Credential deletion

The system SHALL allow a user to delete their own stored credentials and secrets.

#### Scenario: User deletes their GitHub token
- **WHEN** an authenticated user calls `DELETE /api/users/me/github-token`
- **THEN** the system SHALL remove the row; subsequent GitHub operations on behalf of that user SHALL fail with a clear "no token configured" error rather than using a fallback

#### Scenario: User deletes a project secret
- **WHEN** an authenticated user calls `DELETE /api/projects/{id}/secrets/{name}`
- **THEN** the system SHALL remove the row scoped to `(current_user, project_id, name)`

### Requirement: Credential encryption

All credential and secret values SHALL be encrypted at rest using ASP.NET DataProtection. Raw values SHALL NOT appear in database columns.

#### Scenario: Stored ciphertext is not plaintext-readable
- **WHEN** a stored credential row is read directly from Postgres (e.g. via `psql`)
- **THEN** the value column SHALL contain ciphertext unreadable without the DataProtection key ring

### Requirement: Key ring survives restarts

The system's DataProtection key ring SHALL be stored in Postgres so that encrypted credentials remain decryptable across container restarts.

#### Scenario: Credentials decrypt after full restart
- **WHEN** a user stores a credential
- **AND** all Homespun containers are fully restarted (keys not in memory)
- **THEN** the system SHALL successfully decrypt the stored credential using keys loaded from Postgres
