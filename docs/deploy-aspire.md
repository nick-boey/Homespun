# Aspire + azd Deployment (Azure Container Apps)

Deploy Homespun to Azure Container Apps using the .NET Aspire AppHost and Azure Developer CLI (`azd`). This is an alternative to the [VM-based deployment](AZURE_DEPLOYMENT.md).

## Table of contents

- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Custom Dockerfiles](#custom-dockerfiles)
- [Secrets management](#secrets-management)
- [Persistent storage](#persistent-storage)
- [Day-2 operations](#day-2-operations)
- [Teardown](#teardown)
- [VM vs ACA deployment](#vm-vs-aca-deployment)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Requirement | Notes |
|---|---|
| **Azure Developer CLI (azd)** | [Install azd](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd) |
| **Azure CLI** | [Install Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) |
| **.NET 10 SDK** | [Install .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Docker** | Required for building container images |
| **Azure subscription** | With permissions to create Container Apps, Container Registry, and related resources |
| **GitHub PAT** | Personal access token with `repo` scope |
| **Claude Code OAuth token** | Run `claude login`, copy from `~/.claude/.credentials.json` |

## Quick start

### 1. Authenticate

```bash
azd auth login
az login
```

### 2. Initialize the environment

```bash
azd init
```

Select an environment name (e.g., `homespun-dev`). azd reads the Aspire AppHost topology and prepares the deployment.

### 3. Set secrets

```bash
azd env set GITHUB_TOKEN "ghp_your_token_here"
azd env set CLAUDE_CODE_OAUTH_TOKEN "your_oauth_token_here"
```

These are mapped to the `github-token` and `claude-oauth-token` parameters in the AppHost.

### 4. Deploy

```bash
azd up
```

This provisions all Azure resources and deploys the containers. On first run, expect ~10 minutes for infrastructure provisioning and image builds.

### 5. Access the application

After deployment, azd prints the service endpoints. The `web` service URL is the React frontend.

## Architecture

The Aspire AppHost (`src/Homespun.AppHost/Program.cs`) defines three services:

| Service | Description | Image source |
|---|---|---|
| **server** | ASP.NET Core API + SignalR | Custom Dockerfile (root `Dockerfile` + `Dockerfile.base`) |
| **worker** | TypeScript Hono sidecar | Custom Dockerfile (`src/Homespun.Worker/Dockerfile`) |
| **web** | React frontend (nginx) | Custom Dockerfile (`src/Homespun.Web/Dockerfile`) |

azd translates this topology into:

- **Azure Container Apps Environment** — shared networking and logging
- **Azure Container Registry** — stores built images
- **Azure Container Apps** — one per service, with service discovery via Aspire
- **Azure Files** — persistent volume for application data

## Configuration

### AppHost parameters

The AppHost declares parameters in `Program.cs` that azd surfaces during `azd up`:

| Parameter | Description |
|---|---|
| `github-token` | GitHub PAT for PR operations |
| `claude-oauth-token` | Claude Code OAuth token |

Set via `azd env set` or interactively during provisioning.

### Container resources

Container resource limits are configured by azd based on Aspire defaults. Override via Azure portal or Bicep customization after initial deployment if needed. The existing Docker Compose configuration uses 4GB memory / 2.0 CPU as a reference.

## Custom Dockerfiles

All three services use custom Dockerfiles rather than azd auto-build:

- **server**: Multi-stage build from `Dockerfile` at repository root, depends on `Dockerfile.base` for the shared runtime image
- **worker**: Multi-stage build from `src/Homespun.Worker/Dockerfile`, also extends `Dockerfile.base`
- **web**: Multi-stage build from `src/Homespun.Web/Dockerfile` (Node.js build + nginx)

### Base image

The server and worker share a base image (`Dockerfile.base`) containing .NET SDK, Node.js, GitHub CLI, Claude Code CLI, Playwright MCP, Fleece CLI, and Docker CLI. When deploying with azd, the base image is built as part of the Dockerfile multi-stage build chain.

For CI/CD, pre-build and push the base image to ACR:

```bash
az acr build --registry <acr-name> --image homespun-base:latest -f Dockerfile.base .
```

Then reference it via `--build-arg BASE_IMAGE=<acr-name>.azurecr.io/homespun-base:latest`.

## Secrets management

Secrets are stored as ACA secrets and injected as environment variables:

| Secret | Environment variable | Used by |
|---|---|---|
| `github-token` | `GITHUB_TOKEN` | server |
| `claude-oauth-token` | `CLAUDE_CODE_OAUTH_TOKEN` | server, worker |

To rotate secrets:

```bash
azd env set GITHUB_TOKEN "new_token_here"
azd deploy
```

## Persistent storage

The AppHost declares a `homespun-data` volume mounted at `/data` on the server container. azd provisions this as an Azure Files share within the Container Apps environment.

This matches the existing `HOMESPUN_DATA_PATH=/data/homespun-data.json` pattern used in Docker Compose.

## Day-2 operations

### Deploy code changes

```bash
azd deploy
```

This rebuilds images and updates the Container Apps without reprovisioning infrastructure.

### View logs

```bash
azd monitor --logs
```

Or use the Azure portal Container Apps log stream.

### Scale

Container Apps support scale rules. Configure via Azure portal or add scale rules to the Aspire AppHost:

```csharp
server.WithMinReplicas(1).WithMaxReplicas(3);
```

## Teardown

```bash
azd down
```

This removes all Azure resources created by `azd up`. Add `--purge` to also delete soft-deleted resources.

## VM vs ACA deployment

| Aspect | VM (`deploy-infra.sh`) | ACA (`azd up`) |
|---|---|---|
| **Compute** | Single Azure VM | Managed Container Apps |
| **Scaling** | Manual (resize VM) | Auto-scale per service |
| **Cost** | Fixed VM cost | Pay per consumption |
| **Docker socket** | Available (DooD) | Not available |
| **Agent execution** | Docker containers on VM | ACA Jobs (requires adaptation) |
| **Setup complexity** | SSH + cloud-init | Single `azd up` command |
| **Best for** | Development, single user | Production, multi-user |

The VM deployment supports Docker-in-Docker agent execution, which is not directly available in ACA. The ACA deployment requires the ACA agent execution service (issue xJ1xoN) to run agents as ACA Jobs instead of Docker containers.

## Troubleshooting

### azd up fails during provisioning

```bash
# Check azd logs
azd show --output json

# Check Azure resource status
az containerapp list --resource-group <rg-name> --output table
```

### Container fails to start

```bash
# Check container logs
az containerapp logs show --name <app-name> --resource-group <rg-name>

# Check container app status
az containerapp show --name <app-name> --resource-group <rg-name> --query "properties.runningStatus"
```

### Base image build fails

The server Dockerfile expects a base image. If the build fails with a missing base image error, build it first:

```bash
docker build -t homespun-base:local -f Dockerfile.base .
```

For ACR builds, use `az acr build` to build in the cloud without needing a local Docker daemon.

### Services can't communicate

Aspire service discovery configures DNS-based routing within the ACA environment. Verify:

```bash
az containerapp show --name <app-name> --resource-group <rg-name> --query "properties.configuration.ingress"
```

Each service should have internal ingress enabled for inter-service communication.
