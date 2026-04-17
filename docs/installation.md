# Installation and Setup Guide

This guide covers setting up Homespun from a fresh clone through a fully running deployment using Docker Compose.

## Table of contents

- [Prerequisites](#prerequisites)
- [Clone and initial setup](#clone-and-initial-setup)
- [Docker Compose startup](#docker-compose-startup)
- [Verification steps](#verification-steps)
- [Optional: PLG logging stack](#optional-plg-logging-stack)
- [Configuration reference](#configuration-reference)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| **Docker** | 20.10+ | With BuildKit support (`DOCKER_BUILDKIT=1`) |
| **Docker Compose** | v2+ | Included with Docker Desktop; Linux may need separate install |
| **Git** | 2.x+ | For cloning the repository |
| **GitHub PAT** | — | Personal access token with `repo` scope ([create one here](https://github.com/settings/tokens)) |
| **Claude Code OAuth token** | — | Run `claude login` on your host, then copy the token from `~/.claude/.credentials.json` |

### Optional

| Requirement | Notes |
|---|---|
| **Tailscale auth key** | For VPN-based remote access. [Generate a key](https://login.tailscale.com/admin/settings/keys). |

> **Note:** You do not need .NET or Node.js installed on your host machine — all build tooling runs inside Docker containers.

## Clone and initial setup

### 1. Clone the repository

```bash
git clone https://github.com/nick-boey/Homespun.git
cd Homespun
```

### 2. Configure environment

Copy the environment template and fill in your values:

```bash
cp .env.example .env
```

Edit `.env` and set at minimum:

```bash
# Required: GitHub personal access token (needs 'repo' scope)
GITHUB_TOKEN=ghp_your_token_here

# Required: Claude Code OAuth token
# Run 'claude login' on the host, then copy from ~/.claude/.credentials.json
CLAUDE_CODE_OAUTH_TOKEN=your_oauth_token_here
```

`.env` at the repo root is the sole credential source for every deployment path (`run.sh`, `run.ps1`, `run-komodo.sh`, `deploy-infra.sh`, and the Azure VM cloud-init).

### 3. Create the Docker network

Homespun services communicate over a shared Docker network:

```bash
docker network create homespun-net
```

### 4. Create the data directory

```bash
mkdir -p ~/.homespun-container/data
```

## Docker Compose startup

### Quick start with `run.sh` (recommended)

The `scripts/run.sh` script handles building, configuration, and startup:

```bash
# Production: uses pre-built GHCR images with PLG logging
./scripts/run.sh

# Development: build images locally
./scripts/run.sh --local

# Without PLG logging stack
./scripts/run.sh --no-plg

# Interactive mode (foreground, see logs directly)
./scripts/run.sh -it

# Stop all containers
./scripts/run.sh --stop

# View logs
./scripts/run.sh --logs
```

Run `./scripts/run.sh --help` for all options.

**Windows users:** Use `scripts/run.ps1` instead.

### Manual Docker Compose

If you prefer to run Docker Compose directly:

```bash
# Build the base image first
DOCKER_BUILDKIT=1 docker build -t homespun-base:local -f Dockerfile.base .

# Build the application images
docker compose build

# Start core services (server, worker, web)
docker compose up -d

# Start with PLG logging stack
docker compose --profile plg up -d
```

### Services overview

Docker Compose runs the following services:

| Service | Container | Port | Description |
|---|---|---|---|
| **homespun** | `homespun` | `8080` | ASP.NET backend — API, SignalR hubs, agent orchestration |
| **worker** | `homespun-worker` | — | TypeScript sidecar for mini-prompts and lightweight AI tasks |
| **web** | `homespun-web` | `3001` | React frontend (Vite production build served via nginx) |

The **worker** container is an internal service (no host port exposed) that the server communicates with over the `homespun-net` Docker network.

#### PLG logging stack (optional, `--profile plg`)

| Service | Container | Port | Description |
|---|---|---|---|
| **loki** | `homespun-loki` | `3100` | Log aggregation backend |
| **promtail** | `homespun-promtail` | — | Log collector (reads Docker container logs) |
| **grafana** | `homespun-grafana` | `3000` | Log visualization dashboard (default login: `admin`/`admin`) |

### Volume mounts

The main `homespun` container mounts several host paths:

| Mount | Purpose |
|---|---|
| `~/.homespun-container/data` → `/data` | Persistent data (JSON data file, Fleece issues, data protection keys) |
| `~/.ssh` → `/home/homespun/.ssh` (read-only) | SSH keys for git operations |
| `/var/run/docker.sock` → `/var/run/docker.sock` | Docker socket for spawning agent containers (DooD pattern) |

## Verification steps

### 1. Check container health

```bash
docker ps
```

All three core containers (`homespun`, `homespun-worker`, `homespun-web`) should show `Up` status. The `homespun` container should show `(healthy)` after the health check passes (~30 seconds).

### 2. Confirm the server is running

```bash
curl http://localhost:8080/health
```

Expected response: `Healthy` with HTTP 200.

### 3. Confirm the frontend is accessible

Open [http://localhost:3001](http://localhost:3001) in your browser. You should see the Homespun UI.

### 4. Confirm the worker is connected

The worker health check runs automatically. Verify it directly:

```bash
docker exec homespun-worker curl -s http://localhost:8080/api/health
```

You can also check that the server can reach the worker by looking at the server logs:

```bash
docker logs homespun 2>&1 | grep -i worker
```

### 5. Test GitHub connectivity

Create a project in the UI and trigger a GitHub sync to verify your `GITHUB_TOKEN` is working correctly.

## Optional: PLG logging stack

To enable the Promtail-Loki-Grafana logging stack:

```bash
# Start with PLG profile
docker compose --profile plg up -d

# Or via run.sh (PLG is enabled by default)
./scripts/run.sh
```

Access Grafana at [http://localhost:3000](http://localhost:3000) (default credentials: `admin`/`admin`). Loki is pre-configured as a data source.

## Configuration reference

### Environment variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `GITHUB_TOKEN` | Yes | — | GitHub PAT with `repo` scope |
| `CLAUDE_CODE_OAUTH_TOKEN` | Yes | — | Claude Code OAuth token |
| `HOST_PORT` | No | `8080` | Host port for the backend API |
| `WEB_PORT` | No | `3001` | Host port for the React frontend |
| `DATA_DIR` | No | `~/.homespun-container/data` | Persistent data directory |
| `CONTAINER_NAME` | No | `homespun` | Main container name |
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | ASP.NET environment |
| `HSP_EXTERNAL_HOSTNAME` | No | — | External hostname for agent URLs (e.g., `homespun.example.com`) |
| `TAILSCALE_AUTH_KEY` | No | — | Tailscale auth key for VPN access |
| `GRAFANA_PORT` | No | `3000` | Grafana dashboard port |
| `GRAFANA_ADMIN_PASSWORD` | No | `admin` | Grafana admin password |

For Azure VM deployments, the same `.env` is written onto the VM at `/opt/homespun/repo/.env` by cloud-init — no additional configuration mechanism. See [AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md).

## Troubleshooting

### Container fails to start

```bash
# View logs for a specific container
docker logs homespun
docker logs homespun-worker
docker logs homespun-web

# Run interactively to debug
docker run -it --rm --entrypoint /bin/bash homespun:local
```

### Health check fails

```bash
# Check if the app is listening inside the container
docker exec homespun curl -s http://localhost:8080/health

# Check worker health
docker exec homespun-worker curl -s http://localhost:8080/api/health
```

### Docker socket permission denied

The `homespun` container needs access to the Docker socket for spawning agent containers. Ensure the `DOCKER_GID` matches your host's Docker group:

```bash
# Find your Docker group ID
getent group docker | cut -d: -f3

# Pass it to Docker Compose
DOCKER_GID=$(getent group docker | cut -d: -f3) docker compose up -d
```

### GitHub sync not working

- Verify `GITHUB_TOKEN` is set: `docker exec homespun printenv GITHUB_TOKEN | head -c 10`
- Ensure the token has `repo` scope
- Check the token hasn't expired

### SignalR/WebSocket connection failures

- Ensure no reverse proxy is stripping WebSocket upgrade headers
- Check that `/hubs/` paths have extended timeouts in any proxy configuration

### Network not found

If you see `network homespun-net not found`:

```bash
docker network create homespun-net
```

## Windows development with `--with-worker`

`scripts/mock.ps1 --WithWorker` (or `./scripts/mock.sh --with-worker` under WSL/Git Bash) boots a real `homespun-worker` container via docker-compose and points the host-side backend at it using `AgentExecution:Mode=SingleContainer`. This is the only supported way to exercise the live Claude Agent SDK pipeline when the Homespun backend runs on the host via `dotnet run` (i.e. on Windows without Docker-in-Docker).

### Requirements

- Docker Desktop running.
- `$env:CLAUDE_CODE_OAUTH_TOKEN` set to a valid Claude Code OAuth token. The script aborts immediately if the token is missing.
- `WORKER_HOST_PORT` environment variable is optional — defaults to `8081`.

### Behaviour and constraints

- **Single active session.** `SingleContainerAgentExecutionService` enforces at most one concurrent agent session. Starting a second session while one is active raises `SingleContainerBusyException`, surfaced to the UI as an error toast.
- **Dev-only.** The mode is gated on `ASPNETCORE_ENVIRONMENT=Development`. Setting `AgentExecution:Mode=SingleContainer` in Production causes the server to throw at startup.
- **Windows path mapping caveat.** The compose worker has a Linux filesystem view. Live sessions operate on paths inside the container (typically `/workdir/...`), not on Windows host paths. Tool calls like `Read`, `Write`, and `Bash` therefore see the container's filesystem, which is acceptable for pipeline-debugging but not for edit-loop workflows that expect host-side files.
- **Cleanup.** The script runs `docker compose stop worker` on exit via a `try`/`finally` block (PowerShell) or an `EXIT`/`INT`/`TERM` trap (bash). Ctrl+C is safe.
