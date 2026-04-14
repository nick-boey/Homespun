## ADDED Requirements

### Requirement: Execution backend selectable via configuration

The system SHALL select between Docker and ACI agent execution backends via `AgentExecution:Mode` configuration, with valid values `Docker` and `Aci`.

#### Scenario: Docker mode selected
- **WHEN** the server starts with `AgentExecution:Mode=Docker`
- **THEN** the `IAgentExecutionService` resolved from DI SHALL be `DockerAgentExecutionService`

#### Scenario: ACI mode selected
- **WHEN** the server starts with `AgentExecution:Mode=Aci`
- **THEN** the `IAgentExecutionService` resolved from DI SHALL be `AciAgentExecutionService`

#### Scenario: Invalid mode fails startup
- **WHEN** the server starts with an unrecognized `AgentExecution:Mode`
- **THEN** the system SHALL fail startup with a clear configuration error

### Requirement: Server provisions one ACI container group per (user, issue)

When ACI mode is active, the system SHALL create one Azure Container Instance container group per `(userId, issueId)` pair, naming it `homespun-worker-{userIdShort}-{issueId}`.

#### Scenario: First session on an issue creates a container group
- **WHEN** a user starts an agent session for an issue with no existing worker
- **THEN** the system SHALL call ARM to create a container group named `homespun-worker-{userIdShort}-{issueId}` in the configured workers resource group
- **AND** the container group SHALL be configured with CPU and memory from `AgentExecution:Aci:Resources`

#### Scenario: Subsequent session on the same issue reuses the container group
- **WHEN** a user starts a second agent session for the same issue within the idle window
- **AND** the container group for that `(userId, issueId)` exists and is `Running`
- **THEN** the system SHALL reuse the existing container group without a new ARM create call

#### Scenario: Failed container group is replaced
- **WHEN** a new session starts AND an existing container group for that `(userId, issueId)` is in `Failed` state
- **THEN** the system SHALL delete the failed group and create a fresh one

### Requirement: VNet-attached private networking

ACI worker container groups SHALL be deployed into a VNet subnet delegated to `Microsoft.ContainerInstance/containerGroups`. The server SHALL communicate with workers via private IP (or private FQDN) — no public ingress on workers.

#### Scenario: Container group is created in the delegated subnet
- **WHEN** the system creates a container group
- **THEN** the ARM request SHALL include `SubnetIds` referencing the configured `AgentExecution:Aci:SubnetId`
- **AND** the resulting container group SHALL have an `ipAddress.type = 'Private'`

#### Scenario: Server reaches worker via private IP
- **WHEN** a container group reports `ipAddress.ip = W.X.Y.Z`
- **THEN** the server SHALL make HTTP requests to `http://W.X.Y.Z:8000` using the existing SSE protocol

#### Scenario: No public endpoint is exposed
- **WHEN** a container group is created
- **THEN** the ARM request SHALL NOT include a public IP configuration

### Requirement: Managed identity for ARM operations

The server SHALL authenticate to ARM using a system-assigned managed identity. The MI SHALL have role assignments scoped to the workers resource group (container instance operations) and the ACR (image pull).

#### Scenario: ARM calls use the server's managed identity
- **WHEN** the system creates, reads, or deletes a container group
- **THEN** the ARM client SHALL be constructed with `DefaultAzureCredential` or `ManagedIdentityCredential`
- **AND** SHALL NOT use service principal secrets from configuration

#### Scenario: Insufficient RBAC surfaces a clear error
- **WHEN** the server attempts an ARM operation and receives HTTP 403
- **THEN** the system SHALL log a clear diagnostic identifying the missing permission and fail the session start

### Requirement: Azure Files NFS mount for per-user per-issue .claude state

Each worker container group SHALL mount a subpath of the Azure Files NFS share at `/home/homespun/.claude`, where the subpath is `users/{userId}/issues/{issueId}/.claude`.

#### Scenario: Mount path is user-and-issue scoped
- **WHEN** a container group is created for user A on issue X
- **THEN** the ARM request SHALL include a volume mount from `users/{userAId}/issues/{X}/.claude` on the Files share to `/home/homespun/.claude` in the container

#### Scenario: Mount subpath is created if missing
- **WHEN** a session starts for a `(userId, issueId)` pair that has no existing directory on the share
- **THEN** the system SHALL create the subpath on the share before starting the container group

#### Scenario: Claude state persists across worker restarts
- **WHEN** a session completes, the container group is deleted, and a new session starts later on the same issue
- **THEN** the new worker SHALL see the previous `.claude` directory contents at its mount point

### Requirement: Credential injection via secure environment variables

At worker launch, the system SHALL resolve the current user's GitHub token, Claude Code OAuth token, and project secrets from Postgres, decrypt them, and pass them to the ACI container group as `secureValue` environment variables.

#### Scenario: Current user's tokens are injected
- **WHEN** a worker is launched for user A on project P
- **THEN** the ACI env `GITHUB_TOKEN` SHALL equal the decrypted value from user A's `user_github_tokens` row
- **AND** the ACI env `CLAUDE_CODE_OAUTH_TOKEN` SHALL equal the decrypted value from user A's `user_claude_tokens` row

#### Scenario: Current user's project secrets are injected
- **WHEN** a worker is launched for user A on project P
- **THEN** for each `UserSecret` belonging to user A and project P with name `N`, an env var `USER_SECRET_{N}` SHALL be set to its decrypted value

#### Scenario: Another user's secrets are not injected
- **WHEN** a worker is launched for user A
- **THEN** NO secrets belonging to users other than A SHALL be set as env vars

#### Scenario: Secure env values are not returned by subsequent ARM reads
- **WHEN** the system creates a container group with secure env vars
- **AND** later reads the container group via ARM
- **THEN** the returned env values for secure vars SHALL be empty or redacted by Azure

### Requirement: Init script materializes .credentials.json

The worker container image SHALL include an entrypoint init step that, if `$CLAUDE_CODE_OAUTH_TOKEN` is set, writes a valid `.credentials.json` into `~/.claude/` before starting the worker process.

#### Scenario: Credentials file is created on startup
- **WHEN** a container starts with `CLAUDE_CODE_OAUTH_TOKEN=xyz` set
- **THEN** `/home/homespun/.claude/.credentials.json` SHALL contain a JSON document with the token in the format expected by Claude Code
- **AND** the file SHALL have mode 0600

#### Scenario: Missing token fails startup
- **WHEN** a container starts without `CLAUDE_CODE_OAUTH_TOKEN` set
- **THEN** the init script SHALL exit non-zero with a diagnostic message, causing ARM provisioning to fail

### Requirement: ACR pull via managed identity

The worker container image SHALL be pulled from ACR using the server's managed identity, not admin credentials.

#### Scenario: Container group is created with MI-based ACR pull
- **WHEN** the system creates a container group
- **THEN** the ARM request SHALL include `imageRegistryCredentials` referencing the server's managed identity with `identityType='SystemAssigned'`
- **AND** SHALL NOT include a `username`/`password` pair

### Requirement: Lifecycle operations

The system SHALL support start, stop, and idle-eviction of ACI worker container groups.

#### Scenario: Start blocks until worker is ready
- **WHEN** the system provisions a new container group
- **THEN** the start operation SHALL poll provisioning state until it is `Succeeded` AND the worker's `/health` endpoint returns HTTP 200
- **AND** SHALL time out after 60 seconds with a clear error if either condition is not met

#### Scenario: Stop deletes the container group
- **WHEN** a user explicitly stops a session OR the idle timeout expires
- **THEN** the system SHALL call ARM `Delete` on the container group

#### Scenario: Orphan sweep runs on a timer
- **WHEN** the janitor interval elapses (default 2 minutes)
- **THEN** the system SHALL enumerate container groups in the workers resource group
- **AND** SHALL delete any group in `Failed` state or exceeding the hard TTL
- **AND** SHALL delete any group whose `(userId, issueId)` does not correspond to an active session

#### Scenario: Startup reconciles with existing container groups
- **WHEN** the server starts
- **THEN** the system SHALL list existing container groups in the workers RG AND reconcile them against active sessions in Postgres, deleting any without an active session

### Requirement: Observability via Log Analytics

ACI container groups SHALL be created with a Log Analytics workspace diagnostic setting so that stdout/stderr ships to the configured workspace.

#### Scenario: Container group is created with Log Analytics wired up
- **WHEN** the system creates a container group
- **THEN** the ARM request SHALL include a `diagnostics.logAnalytics` property referencing the configured workspace id and shared key

#### Scenario: Container group is tagged for filtering
- **WHEN** the system creates a container group
- **THEN** the ARM request SHALL include tags `homespun.userId`, `homespun.issueId`, and `homespun.projectId` with the correct values

### Requirement: Docker mode unchanged

The existing `DockerAgentExecutionService` behavior SHALL NOT change as a result of this addition. VM deployments continue to work as before.

#### Scenario: VM deployment with `AgentExecution:Mode=Docker` behaves identically
- **WHEN** a VM deployment starts with the new code and `AgentExecution:Mode=Docker`
- **THEN** agent sessions SHALL start and run identically to the previous release

### Requirement: aca.likec4 reflects ACI topology

The `docs/architecture/views/aca.likec4` model SHALL be updated to show the worker as a dynamically-provisioned ACI resource rather than a Container App.

#### Scenario: ACA model includes the ACI worker node
- **WHEN** a reader examines `docs/architecture/model/deployment-aca.likec4`
- **THEN** the worker SHALL be represented as an `aci` (or equivalent) node attached to the `aci-subnet`, not as a `containerApp`
