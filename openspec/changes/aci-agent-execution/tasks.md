## 1. Dependencies & Configuration

- [ ] 1.1 Add NuGet packages: `Azure.ResourceManager`, `Azure.ResourceManager.ContainerInstance`, `Azure.ResourceManager.Network`, `Azure.Identity` to `Homespun.Server.csproj`
- [ ] 1.2 Add `AgentExecution:Mode` to configuration schema (`Docker` | `Aci`)
- [ ] 1.3 Add `AgentExecution:Aci:*` options: `SubscriptionId`, `WorkersResourceGroup`, `SubnetId`, `FileShareName`, `FileShareResourceId`, `AcrLoginServer`, `WorkerImage`, `LogAnalyticsWorkspaceId`, `LogAnalyticsSharedKey`, `Resources:Cpu`, `Resources:MemoryGb`, `IdleTimeoutMinutes`, `HardTtlHours`, `JanitorIntervalMinutes`
- [ ] 1.4 Bind options in `Program.cs` via `Configure<AciAgentExecutionOptions>`; validate on startup

## 2. ACI Execution Service

- [ ] 2.1 Create `Features/ClaudeCode/Services/AciAgentExecutionService.cs` implementing `IAgentExecutionService`
- [ ] 2.2 Implement `ArmClientFactory` that builds an `ArmClient` using `DefaultAzureCredential`
- [ ] 2.3 Implement `GetOrCreateWorkerAsync(userId, issueId, projectId, ...)`:
  - Look up existing container group by deterministic name
  - If `Running` → return cached worker reference
  - If `Failed` or absent → provision fresh
- [ ] 2.4 Implement container-group ARM payload builder: image, resources, env vars (secure), volume mount for Azure Files NFS, subnet ids, log analytics diagnostics, tags, ACR MI pull credentials
- [ ] 2.5 Implement mount-path helper: ensure `users/{userIdShort}/issues/{issueId}/.claude` exists on the share before create
- [ ] 2.6 Implement readiness polling: provisioning state → `/health` probe, 60s timeout
- [ ] 2.7 Implement `StopWorkerAsync` → ARM `Delete`
- [ ] 2.8 Reuse the existing SSE/HTTP client code from `DockerAgentExecutionService` for message streaming (refactor into a shared helper if needed, but do not change wire format)
- [ ] 2.9 Resolve the worker's base URL from the returned private IP (optionally private FQDN)

## 3. DI Factory & Registration

- [ ] 3.1 Create `AgentExecutionFactory` or branch in `Program.cs` that registers `IAgentExecutionService` based on `AgentExecution:Mode`
- [ ] 3.2 Ensure `ContainerDiscoveryService` / `ContainerRecoveryHostedService` only register in Docker mode
- [ ] 3.3 Create `AciJanitorHostedService` (ACI mode only) that enumerates + cleans up orphaned / failed / expired container groups on a timer

## 4. Credential Resolution & Injection

- [ ] 4.1 Extend the credential resolution path (already user-scoped per `multi-user-postgres`) to return a `WorkerCredentials` bundle: decrypted `GITHUB_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, dict of `USER_SECRET_*`
- [ ] 4.2 Pass the bundle into the ARM payload builder; mark all as `secureValue`
- [ ] 4.3 Ensure no decrypted value is logged; add unit test asserting tokens are not present in any log output for the creation path

## 5. Worker Image Init Script

- [ ] 5.1 Update `src/Homespun.Worker/start.sh` to write `~/.claude/.credentials.json` from `$CLAUDE_CODE_OAUTH_TOKEN` (JSON shape verified against Claude Code's expected format)
- [ ] 5.2 `chmod 0600 ~/.claude/.credentials.json`
- [ ] 5.3 Exit non-zero with a clear message if token is missing
- [ ] 5.4 Verify NFS mount is present at `/home/homespun/.claude`; fail fast if mount is missing or unwritable
- [ ] 5.5 Add a test that exercises the init script in isolation (e.g. shell test + docker run)

## 6. IaC — Azure Container Apps / ACI target

- [ ] 6.1 Add Bicep/Terraform module for the shared VNet with two subnets: `aca-subnet` and `aci-subnet` (latter delegated to `Microsoft.ContainerInstance/containerGroups`)
- [ ] 6.2 Provision an Azure Files Premium storage account with an NFS 4.1 share named `homespun-claude`
- [ ] 6.3 Provision a Log Analytics workspace (or reuse existing) and expose workspace id + shared key as outputs
- [ ] 6.4 Create a resource group `homespun-workers-rg` (or configurable name)
- [ ] 6.5 Assign role: server MI → `Contributor` on `homespun-workers-rg` (tighten to a custom role later; document followup)
- [ ] 6.6 Assign role: server MI → `AcrPull` on the ACR
- [ ] 6.7 Assign role: server MI → `Storage File Data SMB Share Contributor` on the Azure Files share
- [ ] 6.8 Optional: Private DNS zone `workers.homespun.internal` linked to the VNet
- [ ] 6.9 Output: `AgentExecution:Aci:SubnetId`, `FileShareName`, `FileShareResourceId`, `WorkersResourceGroup`, `LogAnalyticsWorkspaceId`, `LogAnalyticsSharedKey` for injection into the server Container App

## 7. Architecture Model Updates

- [ ] 7.1 Update `docs/architecture/model/deployment-aca.likec4`: remove `containerApp worker_app`; add `aci worker` element attached to `aci-subnet`
- [ ] 7.2 Update `docs/architecture/model/server-components-aca.likec4`: rename `AcaAgentExecutionService` → `AciAgentExecutionService`; update relationships
- [ ] 7.3 Update `docs/architecture/views/aca.likec4` views as needed
- [ ] 7.4 Run `likec4 validate` to confirm model is consistent

## 8. Tests

- [ ] 8.1 Unit tests: `AciAgentExecutionService` with a mocked `ArmClient` (creates expected payload; reuses on `Running`; replaces on `Failed`)
- [ ] 8.2 Unit tests: credential bundle builder; no secrets logged; secure env markings applied
- [ ] 8.3 Unit tests: janitor identifies orphans correctly
- [ ] 8.4 Integration test (optional live profile): provision a real ACI in a scratch RG, verify `/health`, clean up
- [ ] 8.5 Test: factory selects correct implementation per `AgentExecution:Mode`
- [ ] 8.6 Test: invalid mode fails startup with a clear error

## 9. Documentation

- [ ] 9.1 Add `docs/aca-deployment.md` covering ACI mode: RBAC, subnet sizing, Files share sizing, observability
- [ ] 9.2 Update `docs/AZURE_DEPLOYMENT.md` with the new IaC modules and configuration keys
- [ ] 9.3 Document the credential flow and the init-script contract for worker images
- [ ] 9.4 Add a troubleshooting section: "Worker failed to start" (NFS mount, ARM RBAC, image pull, missing token)

## 10. Pre-PR Verification

- [ ] 10.1 `dotnet test` green, including new `AciAgentExecutionService` tests
- [ ] 10.2 `likec4 validate` passes for updated model
- [ ] 10.3 `cd src/Homespun.Web && npm run lint:fix && npm run format:check && npm run typecheck && npm test`
- [ ] 10.4 Manual smoke in a scratch Azure subscription: deploy IaC, start a session, observe private IP comms, confirm `.claude` persistence across session restarts, confirm logs arriving in Log Analytics
- [ ] 10.5 Manual smoke VM mode: confirm Docker mode still works unchanged with the new factory DI
