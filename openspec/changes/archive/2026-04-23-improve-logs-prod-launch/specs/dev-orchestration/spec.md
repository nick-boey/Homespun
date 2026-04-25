## ADDED Requirements

### Requirement: Prod launch profile boots container topology against persistent data without seeding

The AppHost SHALL provide a `prod` launch profile that runs the container hosting topology (server + web + worker built from local Dockerfiles) against the persistent data directory at `~/.homespun-container/data` with `ASPNETCORE_ENVIRONMENT=Production` and no mock seeding. The profile SHALL be invocable as `dotnet run --project src/Homespun.AppHost --launch-profile prod`. The Seq container SHALL start as it does in every dev profile.

The profile SHALL be discriminated from dev profiles via a new env var `HOMESPUN_PROFILE_KIND=prod` (in addition to `HOMESPUN_DEV_HOSTING_MODE=container`). The AppHost SHALL branch on `HOMESPUN_PROFILE_KIND=prod` inside the container-hosting block to:
- Skip `HOMESPUN_MOCK_MODE`, `MockMode__*`, and `ASPNETCORE_ENVIRONMENT=Mock` env-var injection.
- Set `ASPNETCORE_ENVIRONMENT=Production` on the server container.
- Bind-mount `~/.homespun-container/data` (resolved against the user-profile folder) onto `/data` in the server container.
- Set `HOMESPUN_DATA_PATH=/data/.homespun/homespun-data.json` on the server container.
- Continue running the server container as `--user 0:0` (matches dev-container; required for the docker.sock DooD path).

If the persistent data directory does not exist on the host when the AppHost starts, the AppHost or server SHALL create it (empty); a populated directory SHALL be reused as-is. No seeded demo data SHALL be written.

#### Scenario: prod profile boots full container stack against persistent data

- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile prod`
- **THEN** the AppHost builds and starts the server, web, and worker via `AddDockerfile`
- **AND** the Seq container starts as in any dev profile
- **AND** the server container env contains `ASPNETCORE_ENVIRONMENT=Production` and does NOT contain `HOMESPUN_MOCK_MODE`
- **AND** the server container env contains `HOMESPUN_DATA_PATH=/data/.homespun/homespun-data.json`
- **AND** the server container has a bind mount from `~/.homespun-container/data` on the host to `/data` inside the container
- **AND** no `MockServiceExtensions` registrations execute on the server (live services are wired)

#### Scenario: prod profile creates the data folder on first run

- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile prod` and `~/.homespun-container/data` does not exist on the host
- **THEN** the AppHost or server SHALL create the directory before the server resource enters its Running state
- **AND** the server SHALL start successfully against the empty directory
- **AND** no seeded demo project, issue, or session SHALL appear in the data store

#### Scenario: prod profile reuses existing data unchanged

- **WHEN** a developer runs `dotnet run --project src/Homespun.AppHost --launch-profile prod` and `~/.homespun-container/data/.homespun/homespun-data.json` already contains a populated project list from a prior run
- **THEN** the server SHALL load that data on startup
- **AND** the project list SHALL be visible via `GET /api/projects` after boot

#### Scenario: prod profile starts Seq

- **WHEN** the prod profile is running
- **THEN** Seq SHALL be reachable at `http://localhost:5341`
- **AND** the server SHALL export logs and traces to Seq via `AddSeqEndpoint`

### Requirement: HOMESPUN_DEBUG_FULL_MESSAGES is wired by the AppHost into worker, server, and web env

The AppHost SHALL read the `HOMESPUN_DEBUG_FULL_MESSAGES` environment variable on its own process and SHALL fan it (and its derived implications) out to every relevant resource it provisions. The fan-out SHALL apply to all launch profiles (`dev-mock`, `dev-live`, `dev-windows`, `dev-container`, `prod`).

#### Scenario: AppHost fans HOMESPUN_DEBUG_FULL_MESSAGES out to the server resource

- **WHEN** the AppHost process starts with `HOMESPUN_DEBUG_FULL_MESSAGES=true`
- **THEN** the server resource (whether `AddProject` or `AddDockerfile`) SHALL receive `HOMESPUN_DEBUG_FULL_MESSAGES=true` in its environment
- **AND** the server resource SHALL receive `SessionEventContent__ContentPreviewChars=-1` in its environment when no explicit value is otherwise set

#### Scenario: AppHost fans HOMESPUN_DEBUG_FULL_MESSAGES out to the worker resource

- **WHEN** the AppHost process starts with `HOMESPUN_DEBUG_FULL_MESSAGES=true` under any profile that provisions a worker container
- **THEN** the worker container env SHALL contain `HOMESPUN_DEBUG_FULL_MESSAGES=true`, `DEBUG_AGENT_SDK=true`, and `CONTENT_PREVIEW_CHARS=-1`

#### Scenario: AppHost fans VITE_HOMESPUN_DEBUG_FULL_MESSAGES into the web build

- **WHEN** the AppHost process starts with `HOMESPUN_DEBUG_FULL_MESSAGES=true` under any profile that builds the web bundle
- **THEN** the web build receives `VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true` as a build-time env var

#### Scenario: absence of the umbrella flag preserves today's behaviour

- **WHEN** the AppHost process starts without `HOMESPUN_DEBUG_FULL_MESSAGES`
- **THEN** no `HOMESPUN_DEBUG_FULL_MESSAGES`, `DEBUG_AGENT_SDK`, `CONTENT_PREVIEW_CHARS`, `SessionEventContent__ContentPreviewChars`, or `VITE_HOMESPUN_DEBUG_FULL_MESSAGES` env vars are added by this fan-out logic to any resource
- **AND** existing per-tier defaults (e.g., the worker's `DEBUG_AGENT_SDK=true` set by `dev-windows`) continue to apply unchanged
