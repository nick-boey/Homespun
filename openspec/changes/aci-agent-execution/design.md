## Context

Current execution model (Docker mode) uses Docker-outside-of-Docker: the server container calls `docker run` on the host's Docker socket to spawn per-issue worker containers, reuses them for subsequent sessions on the same issue, and mounts the workdir + `.claude` from the host filesystem. Server-worker communication is HTTP SSE to the worker's container IP.

In ACA, `docker run` is not available. ACA's native `containerApp` primitive is a long-lived app with a stable URL — it's a poor fit for "server lifecycles per-issue containers." Azure Container Instances (ACI) are the right primitive: individually-addressable, per-second-billed, fast-starting containers that the server can provision and tear down at will via ARM.

This change builds on `multi-user-postgres` (which establishes per-user identity + encrypted credential storage). It does NOT change the worker's workdir semantics (clone + push model) — that's a separate change (`worker-clone-and-push`).

## Goals / Non-Goals

**Goals:**
- `IAgentExecutionService` gains a second implementation (`AciAgentExecutionService`) chosen at runtime by config. Existing callers unchanged.
- One ACI container group per `(userId, issueId)`, reused across sessions on the same issue (matches Docker mode's reuse semantics).
- Workers attach to a VNet subnet reachable from the server's ACA environment; communication is private.
- Per-user `~/.claude` persisted on Azure Files NFS; survives worker restarts and lets a crashed worker resume with its session history intact.
- Credentials (GitHub PAT, Claude OAuth token, project secrets) are decrypted from Postgres at worker-launch time and injected as ACI env vars.
- Server identity for ARM calls is a system-assigned managed identity with narrowly-scoped role assignments.

**Non-Goals:**
- Queue-based or polling-based server↔worker comms (stays HTTP SSE, same as Docker mode).
- Warm worker pool or scale rules — server explicitly provisions and tears down per issue.
- Changes to worker workdir semantics (separate change).
- Changes to Docker execution mode (it coexists; no refactor).
- Migration of Docker mode onto the factory pattern in a way that breaks existing VM deployments.

## Decisions

### D1: ACI over Container Apps — confirmed

**Decision:** Dynamically provisioned Azure Container Instances, one container group per worker.

**Rationale:** Already explored in the discovery phase. ACI matches "server-controlled lifecycle, per-issue containers" exactly. Container Apps would require either one app per issue (15–45s provisioning, ARM churn) or a warm pool (not individually addressable, requires inverting the comms model).

**Trade-off:** Diverges from the `aca.likec4` sketch, which modeled worker as a Container App. The likec4 model is updated to reflect reality.

### D2: Server-assigned Managed Identity + RBAC scoped to a workers resource group

**Decision:**
- Server Container App gets a system-assigned managed identity.
- A dedicated `homespun-workers-rg` resource group holds all dynamically created container groups.
- Server MI gets the `Contributor` role (or a narrower custom role limited to `Microsoft.ContainerInstance/*`, `Microsoft.Network/virtualNetworks/subnets/join/action`, `Microsoft.ManagedIdentity/userAssignedIdentities/assign/action`) scoped to `homespun-workers-rg`.
- Server MI additionally gets `AcrPull` scoped to the ACR.

**Rationale:** Limits blast radius. If the server is compromised, the attacker can create/destroy ACI in that RG and pull images — but cannot touch the server's own Container App, Postgres, or Key Vault.

**Alternatives considered:**
- **Subscription-scoped `Contributor`** — overly broad.
- **User-assigned identity shared between server and workers** — simplifies secrets/ACR pulling but couples lifecycles.

### D3: Worker VNet integration via subnet delegation

**Decision:**
- ACA environment is deployed with VNet injection into `aca-subnet`.
- ACI container groups are deployed into a separate `aci-subnet` in the same VNet, delegated to `Microsoft.ContainerInstance/containerGroups`.
- Server addresses workers at their private IP; optional Private DNS zone `workers.homespun.internal` maps `homespun-worker-{userId}-{issueId}.workers.homespun.internal` to the private IP.

**Rationale:** Private-only comms; no public ingress on workers. Same VNet = no peering cost. Separate subnet because ACI delegation is subnet-exclusive.

**Trade-off:** ACI in VNet-delegated subnets have slightly slower start times than public ACI (~5–10s overhead). Accepted.

### D4: Azure Files Premium NFS for `~/.claude`

**Decision:**
- One Azure Files Premium share with NFS 4.1 protocol, mounted at `/mnt/homespun-claude` in every worker.
- Per-worker mount subpath: `<share>/users/<userId>/issues/<issueId>/.claude/` → `/home/homespun/.claude/`.
- Directory created on first worker launch for that `(user, issue)` pair.

**Rationale:** NFS handles small-file workloads (Claude Code writes many small files) far better than SMB. Premium tier gives predictable low latency. Per-issue subpath keeps sessions isolated and matches Docker mode's layout.

**Alternatives considered:**
- **Standard SMB share** — works, but latency on Claude Code's many-small-file pattern was a concern in discovery.
- **Azure Container Storage** — newer; adds infrastructure surface without clear benefit here.

### D5: Credentials flow — Postgres → ARM env vars → init script → filesystem

**Decision:**
1. At worker-launch, server queries `UserGitHubToken`, `UserClaudeToken`, and `UserSecret` rows for the current user.
2. Decrypts via `IDataProtector`.
3. Sets these as ACI environment variables: `GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, and each `USER_SECRET_<NAME>`.
4. ACI env vars are marked `secureValue` so they're not returned from ARM reads after creation.
5. Worker image `start.sh` init script reads `$CLAUDE_CODE_OAUTH_TOKEN` and writes it to `~/.claude/.credentials.json` in the standard Claude Code format, then `exec`s the worker process.

**Rationale:** Matches the pattern the user confirmed in discovery. Keeps all credential material out of mounted volumes.

**Alternatives considered:**
- **Azure Key Vault CSI driver** — cleanest but requires an AKS-style CSI integration that ACI doesn't support natively; would need a per-user Key Vault which is excessive.
- **Pre-generated `.credentials.json` on Azure Files** — couples credential rotation to file-share operations; harder to audit.

### D6: Container group naming + uniqueness

**Decision:**
- Container group name: `homespun-worker-{userId}-{issueId}`.
- `userId` is a short form (first 8 chars of GUID) to stay under ACI's 63-char name limit.
- Before create: server queries ARM for existing container group with that name; if found and running, reuse (matches Docker mode's reuse semantics); if found and stopped, delete + recreate; if not found, create.

**Rationale:** Deterministic names enable reuse without tracking state in Postgres. The reuse-or-replace decision mirrors what `DockerAgentExecutionService` does today.

### D7: Server-to-worker communication stays HTTP SSE

**Decision:** Server makes HTTP requests to `http://<worker-private-ip>:8000` (or `http://<fqdn>:8000`). SSE connections stream tool events back. Identical wire format to Docker mode.

**Rationale:** Zero change to the worker's Hono server, to AG-UI streaming, or to SignalR broadcast path. Easiest possible port from Docker mode.

**Trade-off:** If a worker is killed mid-stream, in-flight SSE is lost. Session state is in Postgres (see `session_messages`), so the next session can continue — matches Docker mode behavior.

### D8: Lifecycle operations and timeouts

**Decision:**
- **Start**: server calls ARM `CreateOrUpdate` on the container group; polls provisioning state with a 60-second budget; returns the private IP once state is `Succeeded` and the worker's `/health` endpoint responds.
- **Stop**: explicit user action or idle-eviction after N minutes of inactivity → server calls ARM `Delete`.
- **Crash recovery**: a separate hosted service scans the workers RG every 2 minutes, removes container groups that are `Failed` or have exceeded a hard TTL (e.g. 24h).
- **Orphan cleanup on startup**: on server startup, list container groups in the workers RG whose `(userId, issueId)` doesn't correspond to any active session; schedule them for deletion.

**Rationale:** Explicit lifecycle + janitorial sweep handles the common failure modes without over-engineering.

### D9: Observability

**Decision:**
- Container group created with a `LogAnalyticsWorkspaceId` property → all stdout/stderr routes to Log Analytics.
- Each container group is tagged with `homespun.userId`, `homespun.issueId`, `homespun.projectId` for filtering in Loki/Log Analytics.

**Rationale:** Matches existing log pipelines; enables per-session log queries from the UI.

## Risks / Trade-offs

- **[Risk] ACI start time spikes during Azure capacity pressure** → Mitigation: 60-second timeout, clear UI indicator "starting worker…", retry once on timeout, surface error if still failing.
- **[Risk] Private IP exhaustion in `aci-subnet` under heavy load** → Mitigation: size the subnet generously (/24 = 250 workers); monitor free IPs via Azure Monitor alert.
- **[Risk] NFS mount failure causes worker boot to hang** → Mitigation: init script has a short NFS-mount health check; if the mount fails, log and exit with a diagnostic code so ARM sees provisioning failure rather than an unresponsive container.
- **[Risk] ARM throttling under high session churn** → Mitigation: cache recent container-group lookups client-side; batch cleanup sweeps instead of per-event deletes.
- **[Risk] Credential leak via ARM auditing / logs** → Mitigation: env var `secureValue`, tag container groups without secrets, never log the decrypted token. Verify with a code-review checklist.
- **[Trade-off] Two execution backends to maintain (Docker + ACI)** → Accepted. Interface is narrow (`IAgentExecutionService`); VM mode stays on Docker indefinitely.
- **[Risk] `DockerAgentExecutionService` registration conflict if both backends register as singletons** → Mitigation: only register the active one based on `AgentExecution:Mode`. Factory does not hold both.

## Migration Plan

1. Ship `AciAgentExecutionService` alongside `DockerAgentExecutionService`. Default config remains Docker.
2. In an ACA deployment, set `AgentExecution:Mode=Aci` and deploy IaC changes (subnet, Files share, MI + RBAC).
3. Start a worker for a test issue; verify provisioning, `.claude` mount, credential injection, SSE stream.
4. Rollback path: flip `AgentExecution:Mode=Docker` — but ACA has no Docker host. Effectively, rollback in ACA means rolling back the whole deployment. In VM, Docker mode is always the default and unaffected.

## Open Questions

- Do we want a user-assigned managed identity attached to each worker ACI so the worker itself can authenticate to Azure services (e.g. Azure OpenAI) without passing tokens? **Working assumption: no for now. Can add later.**
- Should idle-eviction be per-container-group or per-session? **Working assumption: per container group — easier to reason about. Idle = no active session + no HTTP activity for N minutes.**
- Is Log Analytics sufficient, or do workers also ship to Loki? **Working assumption: Log Analytics only in ACA mode; Loki remains VM-mode's log pipeline.**
- What's the default CPU/memory? **Working assumption: 2 vCPU / 4 GB to match current Docker worker resource hints.**
