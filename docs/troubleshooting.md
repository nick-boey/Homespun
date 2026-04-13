# Troubleshooting Guide

This guide covers common issues, debugging techniques, and frequently asked questions for Homespun.

## Table of contents

- [Common setup issues](#common-setup-issues)
- [Agent issues](#agent-issues)
- [GitHub integration issues](#github-integration-issues)
- [General debugging](#general-debugging)
- [FAQ](#faq)

## Common setup issues

### Docker build failures

**"network homespun-net not found"**

The shared Docker network must exist before starting services:

```bash
docker network create homespun-net
```

**BuildKit errors or slow builds**

Ensure BuildKit is enabled:

```bash
export DOCKER_BUILDKIT=1
docker compose build
```

**Base image not found**

The application images depend on `homespun-base:local`. Build it first if using local builds:

```bash
DOCKER_BUILDKIT=1 docker build -t homespun-base:local -f Dockerfile.base .
```

**Fleece CLI version mismatch**

The `Fleece.Core` NuGet package version in `.csproj` files must match the `Fleece.Cli` version installed in `Dockerfile.base`. If you update one, update the other.

### Port conflicts

Default ports used by Homespun:

| Service | Port | Override variable |
|---|---|---|
| Backend API | 8080 | `HOST_PORT` |
| Frontend (nginx) | 3001 | `WEB_PORT` |
| Grafana | 3000 | `GRAFANA_PORT` |
| Loki | 3100 | — |

If a port is already in use, either stop the conflicting service or override the port in your `.env` file:

```bash
HOST_PORT=9080
WEB_PORT=3002
```

To find what's using a port:

```bash
lsof -i :8080
# or
ss -tlnp | grep 8080
```

### Missing environment variables

**Required variables:**

| Variable | How to obtain |
|---|---|
| `GITHUB_TOKEN` | [Create a PAT](https://github.com/settings/tokens) with `repo` scope |
| `CLAUDE_CODE_OAUTH_TOKEN` | Run `claude login`, then copy from `~/.claude/.credentials.json` |

Set these in `.env` at the repo root (see `.env.example`). `run.sh` reads this file on startup.

Verify variables are set inside the container:

```bash
docker exec homespun printenv GITHUB_TOKEN | head -c 10
docker exec homespun printenv CLAUDE_CODE_OAUTH_TOKEN | head -c 10
```

If both are empty, check that your `.env` file is in the repo root and that Docker Compose is picking it up.

### Data directory initialization

The persistent data directory must exist before starting containers:

```bash
mkdir -p ~/.homespun-container/data
```

If you see permission errors on `/data` inside the container, ensure `HOST_UID` and `HOST_GID` match your host user:

```bash
HOST_UID=$(id -u) HOST_GID=$(id -g) docker compose up -d
```

### Docker socket permission denied

The server container needs access to the Docker socket for spawning agent containers. Pass the correct Docker group ID:

```bash
# Find your Docker group ID
getent group docker | cut -d: -f3

# Pass it explicitly
DOCKER_GID=$(getent group docker | cut -d: -f3) docker compose up -d
```

## Agent issues

### Agent not starting

**Container spawn failures**

When agent sessions fail to start, check:

1. **Docker socket access** — The server must reach `/var/run/docker.sock`. See [Docker socket permission denied](#docker-socket-permission-denied).
2. **Worker image availability** — The agent execution service pulls `ghcr.io/nick-boey/homespun-worker:latest` by default. If using local images, start with `--local-agents`.
3. **Resource limits** — Agent containers default to 4 GB memory and 2.0 CPU cores. If the host is under pressure, containers may fail to start.

Check server logs for agent startup errors:

```bash
docker logs homespun 2>&1 | grep -i "agent"
```

**`AgentStartupException`**

This is a retryable error meaning the container failed to launch. Common causes:
- Insufficient host resources (memory/CPU)
- Docker daemon not responding
- Network configuration issues

### Agent session stuck in a state

Agent sessions can get stuck if the container crashes mid-session. To diagnose:

```bash
# List running agent containers
docker ps --filter "name=homespun-agent"

# Check if a specific agent container is healthy
docker inspect --format='{{.State.Status}}' <container-name>
```

**`AgentSessionStateException`** indicates the session is in an invalid state for the requested operation (e.g., trying to send a message to a completed session).

**`AgentTimeoutException`** — Sessions have a maximum duration of 30 minutes (configurable via `AgentExecution:MaxSessionDurationMinutes` in `appsettings.json`). If a session exceeds this, it is terminated automatically.

### Claude Code SDK connection issues

**`AgentConnectionLostException`**

This retryable error means the server lost its connection to the agent container. Possible causes:
- Agent container was killed or OOM-killed
- Network connectivity between containers dropped
- Docker daemon restarted

Check if the container is still running:

```bash
docker ps -a --filter "name=homespun-agent"
```

**`ClaudeCliException`**

The Claude CLI inside the agent container returned an error. Check:
- `CLAUDE_CODE_OAUTH_TOKEN` is valid — tokens can expire; re-run `claude login` on the host
- The Claude API is reachable from inside the container

**Token refresh**

If agent sessions fail with authentication errors:

1. Run `claude login` on your host machine
2. Copy the new token from `~/.claude/.credentials.json`
3. Update `CLAUDE_CODE_OAUTH_TOKEN` in your `.env`
4. Restart the server container: `docker compose restart homespun`

## GitHub integration issues

### Authentication failures

**Token verification**

```bash
# Check the token is set
docker exec homespun printenv GITHUB_TOKEN | head -c 10

# Test the token directly
curl -H "Authorization: token $(cat .env | grep GITHUB_TOKEN | cut -d= -f2)" \
  https://api.github.com/user
```

**Common causes:**
- Token has expired — GitHub classic PATs can have expiration dates
- Token lacks `repo` scope — the full `repo` scope is required for PR operations
- Token was revoked or rotated without updating the container

**Token lookup priority:** The server checks user secrets first, then configuration, then environment variables. Ensure you're setting it in the right place.

### PR sync not working

GitHub sync runs via `GitHubSyncPollingService`, a background hosted service. To diagnose:

1. **Check server logs for sync errors:**

    ```bash
    docker logs homespun 2>&1 | grep -i "github\|sync"
    ```

2. **Verify the project is configured** — PR sync only runs for projects that have a GitHub repository configured in the UI.

3. **Check rate limits** — GitHub API has rate limits (5000 requests/hour for authenticated users). If you're syncing many repositories, you may hit this:

    ```bash
    curl -H "Authorization: token $GITHUB_TOKEN" \
      https://api.github.com/rate_limit
    ```

### Webhook configuration

Homespun uses polling (not webhooks) for GitHub synchronization by default. If you've configured webhooks externally:

- Ensure the webhook URL is reachable from GitHub's servers
- The payload URL should point to your server's external hostname
- Content type should be `application/json`

## General debugging

### Log access

**Container logs (direct)**

```bash
# Follow logs for all services
docker compose logs -f

# Specific service
docker logs -f homespun
docker logs -f homespun-worker
docker logs -f homespun-web
```

Logs are structured JSON with fields: `Timestamp`, `Level`, `Message`, `SourceContext`, `Component`, and optional context fields (`IssueId`, `ProjectName`, `SessionId`).

**Loki (aggregated log queries)**

If the PLG stack is running, query logs via Loki at `http://homespun-loki:3100`:

```bash
# Verify Loki is ready
curl -s http://homespun-loki:3100/ready

# Query recent error logs
curl -s 'http://homespun-loki:3100/loki/api/v1/query_range' \
  --data-urlencode 'query={job="homespun"} |= "Error"' \
  --data-urlencode 'limit=50'
```

**Available Loki labels** (set by Promtail pipeline):
- `level` — Log level (Information, Warning, Error)
- `category` — Source context / logger name
- `component` — Application component
- `issue_id` — Associated Fleece issue ID
- `project_name` — Project name
- `session_id` — Agent session ID

**Using the `/logs` skill**

For interactive log analysis in Claude Code, use the `/logs` skill which provides guided LogQL query building and log exploration.

**Grafana dashboards**

Access Grafana at `http://localhost:3000` (default credentials: `admin`/`admin`). Loki is pre-configured as a data source. Use the Explore view to run LogQL queries.

### SignalR connection troubleshooting

SignalR is used for real-time communication between the frontend and backend (agent sessions, notifications).

**Connection failures:**
- Ensure no reverse proxy is stripping the `Upgrade` header for WebSocket connections
- `/hubs/` paths need extended timeouts in any proxy configuration
- Check CORS — the server configures a `SignalR` CORS policy; ensure your frontend origin is allowed

**Diagnosing SignalR issues in the browser:**
1. Open browser DevTools → Network tab → filter by "WS"
2. Look for the WebSocket connection to `/hubs/claudeCode`
3. Check for failed upgrade requests (HTTP 400/403)

**Internal SignalR URL:**

The server connects to itself via `SignalROptions:InternalBaseUrl` (defaults to `http://localhost:8080`). If this is misconfigured, agent sessions won't receive messages.

### Resetting application state

**Reset all data:**

```bash
# Stop containers
docker compose down

# Remove persistent data
rm -rf ~/.homespun-container/data/*

# Restart
docker compose up -d
```

**Reset a specific project's data:**

The JSON data store is at `~/.homespun-container/data/homespun-data.json`. Back it up before editing.

**Clear agent session cache:**

```bash
rm -rf ~/.homespun-container/data/sessions/*
```

**Remove orphaned agent containers:**

```bash
docker ps -a --filter "name=homespun-agent" -q | xargs -r docker rm -f
```

## FAQ

**Q: Do I need .NET or Node.js installed on my host?**

No. All build tooling runs inside Docker containers. You only need Docker and Docker Compose.

**Q: How do I update to the latest version?**

```bash
git pull
./scripts/run.sh --local   # rebuild and restart
```

Or if using pre-built images:

```bash
docker compose pull
./scripts/run.sh
```

**Q: How do I switch between Docker and local agent execution?**

Set `AgentExecution:Mode` in `appsettings.json` to `Docker` or `Local`, or start with `--local-agents`:

```bash
./scripts/run.sh --local-agents
```

**Q: Where is my data stored?**

Persistent data lives in `~/.homespun-container/data/` on the host, mounted to `/data` in the container. This includes the JSON data store, session cache, data protection keys, and per-issue workspaces.

**Q: How do I access the application in mock mode for development?**

```bash
./scripts/mock.sh
```

This starts the backend at `http://localhost:5101` with seeded demo data. Then start the frontend separately with `cd src/Homespun.Web && npm run dev`.

**Q: What are the Plan vs Build agent modes?**

- **Plan mode** — Read-only access. The agent can read files and suggest changes but cannot modify anything.
- **Build mode** — Full access. The agent can read and write files, run commands, and make commits.

**Q: How do I increase agent session time limits?**

Edit `appsettings.json` and set `AgentExecution:MaxSessionDurationMinutes` to your desired value (default: 30).

**Q: Why is GitHub sync slow or not picking up changes?**

GitHub sync runs on a polling interval. Check the server logs for sync activity. If the token is invalid or rate-limited, sync will silently fail — check [GitHub integration issues](#github-integration-issues).

**Q: How do I check container health?**

```bash
# All containers
docker ps

# Server health endpoint
curl http://localhost:8080/health

# Worker health (from inside the network)
docker exec homespun-worker curl -s http://localhost:8080/api/health
```

**Q: The frontend shows a blank page or connection error**

1. Check that the `homespun-web` container is running: `docker ps`
2. Check nginx logs: `docker logs homespun-web`
3. Verify the backend is reachable: `curl http://localhost:8080/health`
4. Check the browser console for CORS or network errors
