# Homespun Infrastructure

Azure infrastructure for the Homespun application, defined as Bicep templates and deployed via the `scripts/deploy.ps1` script.

## Architecture

```
                        Internet
                            |
                        Tailscale
                            |
    +-----------------------|------------------------+
    |  VNet (10.0.0.0/16)                            |
    |                                                |
    |  +----- aca-infra subnet (10.0.0.0/23) -----+ |
    |  |                                           | |
    |  |  ACA Managed Environment                  | |
    |  |  +-------------------+  HTTP  +---------+ | |
    |  |  | Main App (ca-*)   |------->| Worker  | | |
    |  |  | - Blazor UI       |        | (ca-w-*)| | |
    |  |  | - Tailscale proxy |        | - Agent | | |
    |  |  +--------+----------+        +----+----+ | |
    |  |           |                        |      | |
    |  |           +--- NFS mount: /data ---+      | |
    |  |                                           | |
    |  +-------------------------------------------+ |
    |                                                |
    |  +-- storage-pe subnet (10.0.2.0/24) --+       |
    |  |  Private Endpoint (pe-st-*)         |       |
    |  |  -> Storage Account (NFS)           |       |
    |  +-------------------------------------+       |
    |                                                |
    +------------------------------------------------+

    Key Vault (kv-*)          Log Analytics (log-*)
    - GitHub token            - Container logs
    - Claude OAuth token      - Diagnostics
    - Tailscale auth key
```

## Deployment

### Prerequisites

- Azure CLI (`az`) installed and authenticated
- Credentials available via one of: parameters, environment variables (`HSP_*` or standard), .NET user secrets, or `.env` file

### Deploy

```powershell
# Dev environment
./infra/scripts/deploy.ps1 -ResourceGroup rg-homespun-dev -Environment dev

# Production
./infra/scripts/deploy.ps1 -ResourceGroup rg-homespun-prod -Environment prod

# Preview changes without deploying
./infra/scripts/deploy.ps1 -ResourceGroup rg-homespun-dev -Environment dev -WhatIf
```

The deploy script automatically resolves credentials (GitHub token, Claude OAuth token, Tailscale auth key) from multiple sources in order: explicit parameter, `HSP_*` env vars, standard env vars, .NET user secrets, `.env` file.

### Parameters

Each environment has a parameter file (`main.parameters.dev.json`, `main.parameters.prod.json`) that configures:

| Parameter | Description |
|---|---|
| `baseName` | Base name for resource naming (default: `homespun`) |
| `environmentSuffix` | Environment name (`dev` or `prod`) |
| `mainAppImage` | Container image for the main Blazor app |
| `workerImage` | Container image for the agent worker |
| `agentExecutionMode` | How agents run: `Local`, `Docker`, or `AzureContainerApps` |
| `maxConcurrentSessions` | Max worker replicas for agent scaling |

## Resources

### Networking

**VNet** (`network.bicep`) - Virtual network with two subnets:
- `aca-infra` (/23) - Delegated to `Microsoft.App/environments` for the ACA managed environment. The /23 size is the minimum required by ACA.
- `storage-pe` (/24) - Hosts the private endpoint for NFS storage access.

**Storage Private Endpoint** (`storage-endpoint.bicep`) - Provides private network access to the storage account from within the VNet. NFS protocol requires private endpoint connectivity (public access is not supported). Includes a private DNS zone (`privatelink.file.core.windows.net`) linked to the VNet so containers resolve the storage FQDN to the private IP.

### Storage

**Storage Account** (`storage.bicep`) - Premium FileStorage account with an NFS file share (`homespun-data`, 100 GiB). NFS is used instead of SMB because it provides full POSIX filesystem semantics (chmod, symlinks) required for git operations and Tailscale state persistence. The account has `supportsHttpsTrafficOnly: false` as required by NFS, and network ACLs default to `Deny` since access is through the private endpoint.

### Secrets

**Key Vault** (`keyvault.bicep`) - Stores sensitive credentials as secrets:
- `github-token` - GitHub personal access token for GHCR and API access
- `claude-oauth-token` - OAuth token for Claude Code CLI authentication
- `tailscale-auth-key` - Auth key for Tailscale VPN connectivity

The managed identity is granted the Key Vault Secrets User role. Container apps reference secrets via Key Vault URI, so credentials are never stored in container configuration.

### Identity

**Managed Identity** (`identity.bicep`) - User-assigned managed identity shared by all container apps. Used to authenticate to Key Vault for secret retrieval.

### Compute

**ACA Managed Environment** (`environment.bicep`) - Container Apps environment with:
- VNet integration via the `aca-infra` subnet (internal-only, no public endpoint)
- Log Analytics workspace for container log aggregation
- NFS storage mount (`homespun-storage`) available to all container apps in the environment

**Main Container App** (`containerapp.bicep`) - The Blazor web application:
- Runs the Homespun UI, API, and background services (GitHub sync, Fleece issue tracking)
- Tailscale sidecar provides external HTTPS access (no public ingress)
- NFS volume mounted at `/data` for persistent application data and Tailscale state
- Configured with the worker endpoint for agent session routing
- Scales 1-3 replicas based on HTTP concurrency

**Worker Container App** (`worker-containerapp.bicep`) - Agent execution worker:
- Handles Claude Code agent sessions via HTTP/SSE
- Internal-only ingress (accessible only within the ACA environment)
- Mounts the same NFS volume at `/data` for shared filesystem access with the main app
- Scales 0 to `maxConcurrentSessions` replicas (scales to zero when idle)

### Monitoring

**Log Analytics Workspace** - Collects container logs and diagnostics from the ACA environment.

## Bicep Modules

```
infra/
  main.bicep                      # Orchestrator - wires all modules together
  main.parameters.dev.json        # Dev environment parameters
  main.parameters.prod.json       # Production environment parameters
  modules/
    identity.bicep                # Managed identity
    keyvault.bicep                # Key Vault + secrets
    network.bicep                 # VNet + subnets
    storage.bicep                 # Storage account + NFS file share
    storage-endpoint.bicep        # Private endpoint + DNS zone
    environment.bicep             # ACA environment + NFS storage mount
    containerapp.bicep            # Main Blazor app container
    worker-containerapp.bicep     # Agent worker container
  scripts/
    deploy.ps1                    # Deployment script
```

## Resource Naming

Resources follow the pattern `{type}-{baseName}-{environmentSuffix}`:

| Resource | Dev Name | Type Prefix |
|---|---|---|
| Managed Identity | `id-homespun-dev` | `id-` |
| Key Vault | `kv-homespundev` | `kv-` |
| Storage Account | `sthomespundev` | `st` |
| Log Analytics | `log-homespun-dev` | `log-` |
| VNet | `vnet-homespun-dev` | `vnet-` |
| Private Endpoint | `pe-st-homespun-dev` | `pe-st-` |
| ACA Environment | `cae-homespun-dev` | `cae-` |
| Main Container App | `ca-homespun-dev` | `ca-` |
| Worker Container App | `ca-worker-homespun-dev` | `ca-worker-` |

## Key Design Decisions

- **NFS over SMB**: NFS provides POSIX semantics (chmod, symlinks) needed for git worktrees, Tailscale state directories, and Claude Code operations. SMB lacks these capabilities.
- **VNet integration**: Required for NFS private endpoint access. The ACA environment is internal-only; Tailscale handles all external access.
- **Single worker app**: One worker container app handles multiple agent sessions concurrently via `WorkerSessionService`, scaling horizontally through ACA replicas rather than per-session containers.
- **Shared NFS volume**: Both main app and worker mount the same NFS share, enabling shared filesystem access for git repos, worktrees, and application data.
- **API version 2025-01-01**: The `nfsAzureFile` storage mount type requires this minimum API version for ACA resources. Earlier GA versions (e.g. `2024-03-01`) only support SMB.
