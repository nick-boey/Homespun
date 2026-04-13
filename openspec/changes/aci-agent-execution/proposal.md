## Why

The ACA deployment target needs a way to spawn per-issue agent worker containers. Docker-outside-of-Docker (the current mode) doesn't exist in ACA. Container Apps are too heavyweight for "one worker per issue, lifecycle controlled by the server" — provisioning takes 15–45s per app and replicas aren't individually addressable. Azure Container Instances (ACI) are purpose-built for exactly this pattern: per-second billing, ~10–30s start, individually-addressable private IPs, full image/tooling control. This change introduces an ACI-based execution service alongside the existing Docker service, selectable via configuration.

## What Changes

- Introduce `AciAgentExecutionService` implementing the existing `IAgentExecutionService` interface, so feature code stays agnostic of execution backend.
- Introduce an `AgentExecutionFactory` that selects `Docker` or `Aci` based on `AgentExecution:Mode` configuration; both implementations register in DI, factory resolves the active one.
- Server uses a system-assigned Managed Identity to call Azure Resource Manager (ARM) and create/delete ACI container groups. Each worker = one ACI container group, named `homespun-worker-{userId}-{issueId}`.
- ACI workers attach to the same VNet subnet as the server's ACA environment; server addresses them by private IP (or private FQDN via Azure Private DNS).
- Per-user `~/.claude` persistence via Azure Files NFS mount: `<share>/users/<userId>/issues/<issueId>/.claude`.
- Secrets and tokens (from `multi-user-postgres`'s per-user credential store) are decrypted at worker-launch time and passed as ACI environment variables; worker init script writes `CLAUDE_CODE_OAUTH_TOKEN` into `~/.claude/.credentials.json` on startup.
- ACR pull uses the server's managed identity (ACI supports MI-based pull) — no admin credentials in config.
- Update `aca.likec4` model: remove `containerApp worker_app`, add dynamically-provisioned `aci worker` node with its relationships.
- IaC updates: subnet delegation for ACI, Azure Files Premium NFS share, role assignments for server MI on the resource group, Private DNS Zone for worker FQDN resolution.
- Session recovery on worker restart: mounted `.claude` plus restored `session_messages` from Postgres allow a resumed worker to pick up where it left off.

## Capabilities

### New Capabilities
- `aci-execution`: ACI-based agent worker provisioning, lifecycle, networking, and credential injection. Selectable alongside Docker execution.

### Modified Capabilities
<!-- None. The Docker execution service is unchanged by this proposal. -->

## Impact

- **Backend**: New `Features/ClaudeCode/Services/AciAgentExecutionService.cs` (~similar size to `DockerAgentExecutionService`). New `AgentExecutionFactory` that resolves based on config. `Program.cs` DI branches on `AgentExecution:Mode`.
- **Dependencies**: `Azure.ResourceManager`, `Azure.ResourceManager.ContainerInstance`, `Azure.ResourceManager.Network`, `Azure.Identity`. Optional: `Azure.ResourceManager.PrivateDns` for FQDN-based routing.
- **IaC**: Bicep/Terraform for VNet + subnet, Azure Files Premium (NFS) share, role assignments (`Contributor` on the workers RG scope for server MI), Private DNS zone.
- **Server-worker comms**: HTTP SSE over private IP/FQDN — same code path as Docker mode; only the hostname lookup changes.
- **Container image**: `Dockerfile` (worker) gains a small init script that materializes `.credentials.json` from `$CLAUDE_CODE_OAUTH_TOKEN`. Otherwise the existing worker image works unchanged.
- **Observability**: ACI containers ship logs to Log Analytics via the Log Analytics diagnostic setting on the container group.
- **Cost**: Per-second ACI billing; typical worker ~$0.02/hour for 1 vCPU / 2GB at westus2 pricing. Azure Files Premium NFS share is flat-rate (~$0.20/GiB/month reserved).
- **Security**: No secrets on disk in production — all flow through ACI env vars from Postgres-backed encrypted credential store. Workers have no inbound public access.
- **Testing**: New `Homespun.Tests/Features/ClaudeCode/AciAgentExecutionServiceTests.cs` using a mocked ARM client. Integration smoke: optional live-test profile that provisions a real ACI in a scratch RG and tears it down.
- **Depends on**: `multi-user-postgres` change (needs per-user credentials and user identity to key the worker lifecycle).
