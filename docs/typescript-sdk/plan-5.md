# Phase 5: ACA Execution Service (Dynamic Container Apps)

## Context

The current `AzureContainerAppsAgentExecutionService` routes all requests to a single shared worker Container App. This phase refactors it to dynamically create and delete Container Apps per issue using the Azure Resource Manager SDK, mirroring the per-issue isolation of the Docker mode (Phase 4).

**Dependencies:** Phases 1 (Hono worker), 2 (SdkMessage types), 3 (IssueWorkspaceService)

## 5.1 Approach: Dynamic Container Apps via Azure Management API

Use `Azure.ResourceManager.AppContainers` NuGet package to create and delete Container Apps per issue. Each issue gets its own Container App running the Hono worker image.

## 5.2 Refactor `AzureContainerAppsAgentExecutionService`

### New Configuration
```csharp
public class AzureContainerAppsAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:AzureContainerApps";
    public string EnvironmentId { get; set; }        // ACA managed environment resource ID
    public string WorkerImage { get; set; }          // Hono worker image (ghcr.io/nick-boey/homespun-worker:latest)
    public string ResourceGroupName { get; set; }    // Resource group for dynamic apps
    public string StorageMountName { get; set; }     // NFS storage mount name in the environment
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan ProvisioningTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

### Container App Naming
`ca-issue-{issueId}` (must be lowercase, alphanumeric + hyphens, max 32 chars)

### `StartSessionAsync` Flow
1. Extract `issueId` from `AgentStartRequest`
2. Check if Container App `ca-issue-{issueId}` exists (via ARM API or in-memory cache)
3. If not, create via ARM API with:
   - Worker image
   - NFS volume mount (same share, sub-paths: `projects/{name}/issues/{id}/.claude` and `projects/{name}/issues/{id}/src`)
   - Environment variables: `ISSUE_ID`, `PROJECT_ID`, `PROJECT_NAME`, auth tokens, git identity
   - Internal ingress (port 8080, no external access)
   - Min replicas: 1, Max replicas: 1
   - Secrets from Key Vault (same pattern as current `containerapp.bicep`)
4. Wait for app to be provisioned and healthy (~1-2 minutes)
5. Get the app's internal FQDN
6. Send `POST /sessions` to worker FQDN
7. Stream SSE events as `SdkMessage` types

### Container App Lifecycle

| Action | Behavior |
|--------|----------|
| Stop agent session | `DELETE /sessions/:id` on worker - Container App stays running |
| Stop container | Delete the Container App via ARM API |
| Issue completed | Delete the Container App |
| Idle timeout | Background task deletes Container Apps with no active sessions for > N minutes |

### ARM API Operations

```csharp
// Create Container App
var containerAppData = new ContainerAppData(location)
{
    EnvironmentId = environmentId,
    Configuration = new ContainerAppConfiguration
    {
        Ingress = new ContainerAppIngressConfiguration
        {
            TargetPort = 8080,
            External = false,  // Internal only
        },
        Secrets = { /* GitHub token, Claude OAuth */ },
    },
    Template = new ContainerAppTemplate
    {
        Containers = {
            new ContainerAppContainer
            {
                Name = "worker",
                Image = workerImage,
                Resources = new ContainerAppContainerResources { Cpu = 2.0, Memory = "4Gi" },
                Env = { /* ISSUE_ID, PROJECT_ID, etc. */ },
            }
        },
        Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 1 },
        Volumes = {
            new ContainerAppVolume
            {
                Name = "claude-data",
                StorageType = "NfsAzureFile",
                StorageName = storageMountName,
            }
        },
    },
};

// Delete Container App
await containerAppResource.DeleteAsync(WaitUntil.Started);
```

## 5.3 Bicep Updates

### Remove Static Worker (`infra/modules/worker-containerapp.bicep`)
The static worker container app is no longer needed. The main app creates Container Apps dynamically.

### Update `infra/main.bicep`
- Remove the `worker-containerapp` module reference
- Add role assignment: main app's managed identity needs `Contributor` role on the resource group to create/delete Container Apps
- Pass new configuration to main app:
  - `AgentExecution__AzureContainerApps__EnvironmentId` = environment resource ID
  - `AgentExecution__AzureContainerApps__WorkerImage` = worker image
  - `AgentExecution__AzureContainerApps__ResourceGroupName` = resource group name
  - `AgentExecution__AzureContainerApps__StorageMountName` = NFS storage name

### Update `infra/modules/containerapp.bicep`
- Add the new ACA config environment variables
- Remove the old `WorkerEndpoint` variable

### NFS Storage Considerations
The NFS mount is at the environment level. Dynamic Container Apps in the same environment can reference the same storage mount. Volume mount sub-paths allow per-issue isolation within the single NFS share:
- `.claude` volume mount: `mountPath=/home/homespun/.claude`, `subPath=projects/{name}/issues/{id}/.claude`
- `src` volume mount: `mountPath=/workdir`, `subPath=projects/{name}/issues/{id}/src`

## Critical Files to Modify
- `src/Homespun/Features/ClaudeCode/Services/AzureContainerAppsAgentExecutionService.cs` - Major refactor (~556 lines)
- `infra/modules/worker-containerapp.bicep` - Remove or repurpose
- `infra/main.bicep` - Update references, add role assignment
- `infra/modules/containerapp.bicep` - Add new env vars
- `infra/main.parameters.dev.json` - Update parameters
- `infra/main.parameters.prod.json` - Update parameters

## NuGet Dependencies
- Add `Azure.ResourceManager.AppContainers` to `Homespun.csproj`
- Add `Azure.Identity` (if not already present) for managed identity auth

## Tests
- Container App creation: verify correct ARM resource definition
- Container App naming: verify `ca-issue-{issueId}` format and length constraints
- Provisioning wait: verify health check polling after creation
- SSE parsing: verify SDK message format (same as Docker mode)
- Cleanup: verify Container App deletion on issue completion

## Verification
1. Deploy to dev environment with `infra/scripts/deploy.ps1`
2. Start agent for an issue -> verify Container App `ca-issue-{issueId}` appears in Azure portal
3. Wait for provisioning -> verify health check succeeds
4. Send messages -> verify SSE streaming works
5. Stop session -> verify Container App is still running
6. Start new session on same issue -> verify same Container App is reused
7. Complete issue -> verify Container App is deleted
