#!/bin/bash
set -e

# ============================================================================
# Homespun Docker Compose Runner
# ============================================================================
#
# This script runs Homespun using Docker Compose with optional PLG logging stack
# and Tailscale sidecar for VPN access.
# By default, it uses pre-built GHCR images. PLG is enabled via compose profile.
# Tailscale runs as a standalone sidecar (config/tailscale/compose.yml).
#
# Usage:
#   ./run.sh                    # Production: GHCR images with PLG stack
#   ./run.sh --local            # Development: Build local images, Docker agents
#   ./run.sh --local-agents     # Development: Build local, in-process agents
#   ./run.sh --no-plg           # Run without PLG logging stack
#   ./run.sh --stop             # Stop all containers
#   ./run.sh --logs             # View container logs
#
# Options:
#   --local-agents              Use in-process agent execution (no worker containers)
#   --debug                     Build in Debug configuration
#   --mock                      Run in mock mode with seeded demo data
#   --port PORT                 Override host port (default: 8080)
#   -it, --interactive          Run in interactive mode (foreground)
#   -d, --detach                Run in detached mode (background) [default]
#   --stop                      Stop running containers
#   --logs                      Follow container logs
#   --pull                      Pull latest image before starting
#   --external-hostname HOST    Set external hostname for agent URLs
#   --data-dir DIR              Override data directory (default: ~/.homespun-container/data)
#   --container-name NAME       Override container name (default: homespun)
#   --no-tailscale              Disable Tailscale (do not load auth key)
#   --no-plg                    Disable PLG logging stack (Promtail, Loki, Grafana)
#
# Environment Variables:
#   HSP_GITHUB_TOKEN            GitHub token (preferred for VM secrets)
#   HSP_CLAUDE_CODE_OAUTH_TOKEN Claude Code OAuth token (preferred for VM secrets)
#   HSP_TAILSCALE_AUTH_KEY      Tailscale auth key (preferred for VM secrets)
#   HSP_EXTERNAL_HOSTNAME       External hostname for agent URLs
#   GITHUB_TOKEN                GitHub token (fallback)
#   CLAUDE_CODE_OAUTH_TOKEN     Claude Code OAuth token (fallback)
#   TAILSCALE_AUTH_KEY          Tailscale auth key (fallback)
#
# Configuration File:
#   Place credentials in ~/.homespun/env to auto-load them:
#     export GITHUB_TOKEN=ghp_...
#     export CLAUDE_CODE_OAUTH_TOKEN=...
#     export TAILSCALE_AUTH_KEY=tskey-auth-...
#
# Volume Mounts:
#   SSH directory (~/.ssh) is mounted read-only for git operations
#   Note: We do NOT mount ~/.claude - the container uses its own MCP server config

# Get script directory and repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default values
USE_LOCAL=false
USE_LOCAL_AGENTS=false
USE_DEBUG=false
USE_MOCK=false
NO_TAILSCALE=false
NO_PLG=false
DETACHED=true
ACTION="start"
PULL_FIRST=false
EXTERNAL_HOSTNAME=""
DATA_DIR_PARAM=""
CONTAINER_NAME="homespun"
HOST_PORT="8080"
GRAFANA_PORT="3000"
USER_SECRETS_ID="2cfc6c57-72da-4b56-944b-08f2c1df76f6"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Helper functions
log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

show_help() {
    head -40 "$0" | tail -35
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --local) USE_LOCAL=true ;;
        --local-agents) USE_LOCAL_AGENTS=true ;;
        --debug) USE_DEBUG=true ;;
        --mock) USE_MOCK=true ;;
        --no-tailscale) NO_TAILSCALE=true ;;
        --no-plg) NO_PLG=true ;;
        --port) HOST_PORT="$2"; shift ;;
        -it|--interactive) DETACHED=false ;;
        -d|--detach) DETACHED=true ;;
        --stop) ACTION="stop" ;;
        --logs) ACTION="logs" ;;
        --pull) PULL_FIRST=true ;;
        --external-hostname) EXTERNAL_HOSTNAME="$2"; shift ;;
        --data-dir) DATA_DIR_PARAM="$2"; shift ;;
        --container-name) CONTAINER_NAME="$2"; shift ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# Change to repository root
cd "$REPO_ROOT"

echo
log_info "=== Homespun Docker Compose Runner ==="
echo

# Compose file and env file paths
COMPOSE_FILE="$REPO_ROOT/docker-compose.yml"
ENV_FILE="$REPO_ROOT/.env.compose"

# Tailscale compose file (standalone sidecar)
TAILSCALE_COMPOSE_FILE="$REPO_ROOT/config/tailscale/compose.yml"

# Handle stop action
if [ "$ACTION" = "stop" ]; then
    log_info "Stopping containers..."
    if [ -f "$ENV_FILE" ]; then
        docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" down 2>/dev/null || true
    fi
    docker compose -f "$TAILSCALE_COMPOSE_FILE" down 2>/dev/null || true
    docker stop "$CONTAINER_NAME" 2>/dev/null || true
    docker rm "$CONTAINER_NAME" 2>/dev/null || true
    docker stop homespun-worker homespun-loki homespun-promtail homespun-grafana homespun-tailscale 2>/dev/null || true
    docker rm homespun-worker homespun-loki homespun-promtail homespun-grafana homespun-tailscale 2>/dev/null || true
    log_success "Containers stopped."
    exit 0
fi

# Handle logs action
if [ "$ACTION" = "logs" ]; then
    log_info "Following container logs (Ctrl+C to exit)..."
    if [ -f "$ENV_FILE" ]; then
        docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" logs -f
    else
        docker logs -f "$CONTAINER_NAME"
    fi
    exit 0
fi

# Step 1: Validate Docker is running
log_info "[1/5] Checking Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    exit 1
fi
log_success "      Docker is available."

# Step 2: Check/build image
log_info "[2/5] Checking container images..."
if [ "$USE_LOCAL" = true ]; then
    # Development: Build base + both app images locally
    IMAGE_NAME="homespun:local"
    WORKER_IMAGE="homespun-worker:local"
    BASE_IMAGE="homespun-base:local"
    BUILD_CONFIG="Release"
    if [ "$USE_DEBUG" = true ]; then
        BUILD_CONFIG="Debug"
    fi

    log_info "      Building base tooling image..."
    if ! DOCKER_BUILDKIT=1 docker build -t "$BASE_IMAGE" -f "$REPO_ROOT/Dockerfile.base" "$REPO_ROOT"; then
        log_error "Failed to build base Docker image."
        exit 1
    fi
    log_success "      Base image built: $BASE_IMAGE"

    log_info "      Building main Homespun image ($BUILD_CONFIG)..."
    if ! docker build -t "$IMAGE_NAME" --build-arg BUILD_CONFIGURATION="$BUILD_CONFIG" "$REPO_ROOT"; then
        log_error "Failed to build main Docker image."
        exit 1
    fi
    log_success "      Main image built: $IMAGE_NAME ($BUILD_CONFIG)"

    log_info "      Building Worker image..."
    if ! docker build -t "$WORKER_IMAGE" -f "$REPO_ROOT/src/Homespun.Worker/Dockerfile" "$REPO_ROOT/src/Homespun.Worker"; then
        log_error "Failed to build Worker Docker image."
        exit 1
    fi
    log_success "      Worker image built: $WORKER_IMAGE"
else
    # Production: Use GHCR images
    IMAGE_NAME="ghcr.io/nick-boey/homespun:latest"
    WORKER_IMAGE="ghcr.io/nick-boey/homespun-worker:latest"
    if [ "$PULL_FIRST" = true ]; then
        log_info "      Pulling latest images..."
        docker pull "$IMAGE_NAME"
        docker pull "$WORKER_IMAGE"
    fi
    log_success "      Using GHCR image: $IMAGE_NAME"
    log_success "      Using GHCR worker: $WORKER_IMAGE"
fi

# Step 3: Read credentials
log_info "[3/5] Reading credentials..."

# Source ~/.homespun/env if it exists (recommended location for credentials)
HOMESPUN_ENV_FILE="$HOME/.homespun/env"
if [ -f "$HOMESPUN_ENV_FILE" ]; then
    log_info "      Loading credentials from $HOMESPUN_ENV_FILE"
    source "$HOMESPUN_ENV_FILE"
fi

# GitHub Token: Check environment variables in order of preference
# 1. HSP_GITHUB_TOKEN (for VM secrets)
# 2. GITHUB_TOKEN (standard)
GITHUB_TOKEN="${HSP_GITHUB_TOKEN:-${GITHUB_TOKEN:-}}"

# If not in environment, try reading from .NET user secrets JSON
if [ -z "$GITHUB_TOKEN" ]; then
    SECRETS_PATH="$HOME/.microsoft/usersecrets/$USER_SECRETS_ID/secrets.json"
    if [ -f "$SECRETS_PATH" ]; then
        if command -v python3 &>/dev/null; then
            GITHUB_TOKEN=$(python3 -c "import json, sys; print(json.load(open('$SECRETS_PATH')).get('GitHub:Token', ''))" 2>/dev/null)
        elif command -v jq &>/dev/null; then
            GITHUB_TOKEN=$(jq -r '."GitHub:Token" // empty' "$SECRETS_PATH")
        fi
    fi
fi

# Try reading from .env file
if [ -z "$GITHUB_TOKEN" ] && [ -f "$REPO_ROOT/.env" ]; then
    GITHUB_TOKEN=$(grep -E "^GITHUB_TOKEN=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -z "$GITHUB_TOKEN" ]; then
    log_warn "      GitHub token not found."
    log_warn "      Set GITHUB_TOKEN in ~/.homespun/env or environment."
else
    MASKED_TOKEN="${GITHUB_TOKEN:0:10}..."
    log_success "      GitHub token found: $MASKED_TOKEN"
fi

# Claude Code OAuth Token: Check environment variables
# 1. HSP_CLAUDE_CODE_OAUTH_TOKEN (for VM secrets)
# 2. CLAUDE_CODE_OAUTH_TOKEN (standard)
CLAUDE_CODE_OAUTH_TOKEN="${HSP_CLAUDE_CODE_OAUTH_TOKEN:-${CLAUDE_CODE_OAUTH_TOKEN:-}}"

# Try reading from .env file
if [ -z "$CLAUDE_CODE_OAUTH_TOKEN" ] && [ -f "$REPO_ROOT/.env" ]; then
    CLAUDE_CODE_OAUTH_TOKEN=$(grep -E "^CLAUDE_CODE_OAUTH_TOKEN=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

if [ -z "$CLAUDE_CODE_OAUTH_TOKEN" ]; then
    log_warn "      Claude Code OAuth token not found."
    log_warn "      Set CLAUDE_CODE_OAUTH_TOKEN in ~/.homespun/env or environment."
else
    MASKED_CC_TOKEN="${CLAUDE_CODE_OAUTH_TOKEN:0:15}..."
    log_success "      Claude Code OAuth token found: $MASKED_CC_TOKEN"
fi

# Tailscale Auth Key: Check environment variables (unless --no-tailscale)
if [ "$NO_TAILSCALE" = true ]; then
    TAILSCALE_AUTH_KEY=""
    log_info "      Tailscale disabled (--no-tailscale flag)"
else
    # 1. HSP_TAILSCALE_AUTH_KEY (for VM secrets)
    # 2. TAILSCALE_AUTH_KEY (standard)
    TAILSCALE_AUTH_KEY="${HSP_TAILSCALE_AUTH_KEY:-${TAILSCALE_AUTH_KEY:-}}"

    # Try reading from .env file
    if [ -z "$TAILSCALE_AUTH_KEY" ] && [ -f "$REPO_ROOT/.env" ]; then
        TAILSCALE_AUTH_KEY=$(grep -E "^TAILSCALE_AUTH_KEY=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
    fi

    if [ -z "$TAILSCALE_AUTH_KEY" ]; then
        log_warn "      Tailscale auth key not found (Tailscale will be disabled)."
        log_warn "      Set TAILSCALE_AUTH_KEY in ~/.homespun/env for VPN access."
    else
        MASKED_TS_KEY="${TAILSCALE_AUTH_KEY:0:15}..."
        log_success "      Tailscale auth key found: $MASKED_TS_KEY"
    fi
fi

# Step 4: Set up directories
log_info "[4/5] Setting up directories..."

# Get host user UID/GID early - needed for permission fix and container user
HOST_UID="$(id -u)"
HOST_GID="$(id -g)"

# Use DATA_DIR_PARAM if provided, otherwise default
if [ -n "$DATA_DIR_PARAM" ]; then
    DATA_DIR="$DATA_DIR_PARAM"
else
    DATA_DIR="$HOME/.homespun-container/data"
fi
SSH_DIR="$HOME/.ssh"

if [ ! -d "$DATA_DIR" ]; then
    mkdir -p "$DATA_DIR"
    log_success "      Created data directory: $DATA_DIR"
else
    log_success "      Data directory exists: $DATA_DIR"
fi

chmod 777 "$DATA_DIR" 2>/dev/null || true

# Create DataProtection-Keys directory if it doesn't exist
DATA_PROTECTION_DIR="$DATA_DIR/DataProtection-Keys"
if [ ! -d "$DATA_PROTECTION_DIR" ]; then
    mkdir -p "$DATA_PROTECTION_DIR"
    log_success "      Created DataProtection-Keys directory"
fi

# Fix permissions on data directory to match the user the container will run as
# This ensures both container and host user can access the files
docker run --rm -v "$DATA_DIR:/fixdata" alpine chown -R $HOST_UID:$HOST_GID /fixdata 2>/dev/null && \
    log_success "      Fixed data directory permissions" || true

# Check SSH directory
SSH_MOUNT=""
if [ -d "$SSH_DIR" ]; then
    SSH_MOUNT="-v $SSH_DIR:/home/homespun/.ssh:ro"
    log_success "      SSH directory found: $SSH_DIR"
else
    log_warn "      SSH directory not found: $SSH_DIR"
fi

# Mount Docker socket for DooD (Docker outside of Docker)
# This enables containers to spawn sibling containers using the host's Docker daemon
DOCKER_SOCKET_MOUNT="-v /var/run/docker.sock:/var/run/docker.sock"
DOCKER_GROUP_ADD=""
if [ -S "/var/run/docker.sock" ]; then
    # Get the GID of the docker socket to add to container user for DooD access
    DOCKER_SOCKET_GID="$(stat -c '%g' /var/run/docker.sock)"
    DOCKER_GROUP_ADD="--group-add $DOCKER_SOCKET_GID"
    log_success "      Docker socket found: /var/run/docker.sock (DooD enabled, GID: $DOCKER_SOCKET_GID)"
else
    log_info "      Docker socket will be mounted: /var/run/docker.sock (DooD)"
    log_info "      Note: Socket must exist on host for container Docker access"
fi

# Note: We intentionally do NOT mount the host's ~/.claude directory.
# Reason: The container has its own settings.json with MCP server config (created in Dockerfile).
# Mounting the host's ~/.claude would overwrite this config and cause plugin path issues
# since installed_plugins.json contains absolute host paths that don't exist in the container.
# We mount ONLY the credentials file for OAuth authentication.
CLAUDE_CREDENTIALS_MOUNT=""
CLAUDE_CREDENTIALS_FILE="$HOME/.claude/.credentials.json"
if [ -f "$CLAUDE_CREDENTIALS_FILE" ]; then
    CLAUDE_CREDENTIALS_MOUNT="-v $CLAUDE_CREDENTIALS_FILE:/home/homespun/.claude/.credentials.json:ro"
    log_success "      Claude credentials found: $CLAUDE_CREDENTIALS_FILE"
else
    log_warn "      Claude credentials not found: $CLAUDE_CREDENTIALS_FILE"
    log_warn "      Run 'claude login' to authenticate, or set CLAUDE_CODE_OAUTH_TOKEN"
fi

# Read external hostname
if [ -z "$EXTERNAL_HOSTNAME" ]; then
    EXTERNAL_HOSTNAME="${HSP_EXTERNAL_HOSTNAME:-}"
fi

# Try reading external hostname from .env file if not set
if [ -z "$EXTERNAL_HOSTNAME" ] && [ -f "$REPO_ROOT/.env" ]; then
    EXTERNAL_HOSTNAME=$(grep -E "^HSP_EXTERNAL_HOSTNAME=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
fi

# Step 5: Generate .env.compose and start containers
log_info "[5/5] Starting containers with Docker Compose..."
echo

log_info "======================================"
log_info "  Container Configuration"
log_info "======================================"
echo "  Container:   $CONTAINER_NAME"
echo "  Image:       $IMAGE_NAME"
echo "  User:        $HOST_UID:$HOST_GID (host user)"
echo "  Port:        $HOST_PORT"
echo "  URL:         http://localhost:$HOST_PORT"
echo "  Data mount:  $DATA_DIR"
if [ -d "$SSH_DIR" ]; then
    echo "  SSH mount:   $SSH_DIR (read-only)"
fi
echo "  Docker:      DooD enabled (host socket mounted)"
if [ -f "$CLAUDE_CREDENTIALS_FILE" ]; then
    echo "  Claude:      Credentials mounted (OAuth enabled)"
elif [ -n "$CLAUDE_CODE_OAUTH_TOKEN" ]; then
    echo "  Claude:      OAuth token via environment variable"
else
    echo "  Claude:      No authentication (agents will fail)"
fi
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    echo "  Tailscale:   Enabled (sidecar container)"
fi
if [ -n "$EXTERNAL_HOSTNAME" ]; then
    echo "  Agent URLs:  https://$EXTERNAL_HOSTNAME:<port>"
fi
if [ "$USE_MOCK" = true ]; then
    echo "  Mock mode:   Enabled (seeded demo data)"
fi
if [ "$USE_LOCAL" = true ]; then
    echo "  Build:       Local (development mode)"
else
    echo "  Build:       GHCR (production images)"
fi
if [ "$USE_LOCAL_AGENTS" = true ]; then
    echo "  Agents:      In-process (Local mode)"
else
    echo "  Agents:      Docker containers ($WORKER_IMAGE)"
fi
echo "  Sidecar:     Worker container (mini-prompts at http://homespun-worker:8080)"
if [ "$NO_PLG" = false ]; then
    echo "  PLG Stack:   Enabled (Grafana at http://localhost:$GRAFANA_PORT)"
else
    echo "  PLG Stack:   Disabled"
fi
log_info "======================================"
echo

# Generate .env.compose file for Docker Compose
log_info "Generating $ENV_FILE..."

# Determine ASP.NET environment
if [ "$USE_MOCK" = true ]; then
    ASPNETCORE_ENV="MockLive"
else
    ASPNETCORE_ENV="Production"
fi

# Determine agent mode
if [ "$USE_LOCAL_AGENTS" = true ]; then
    AGENT_MODE="Local"
else
    AGENT_MODE="Docker"
fi

# Write environment file for Docker Compose
cat > "$ENV_FILE" << EOF
# Generated by run.sh on $(date)
# Do not edit manually - regenerated on each run

# Container settings
HOMESPUN_IMAGE=$IMAGE_NAME
WORKER_IMAGE=$WORKER_IMAGE
CONTAINER_NAME=$CONTAINER_NAME
HOST_PORT=$HOST_PORT
HOST_UID=$HOST_UID
HOST_GID=$HOST_GID
DOCKER_GID=$DOCKER_SOCKET_GID

# Directories
DATA_DIR=$DATA_DIR
SSH_DIR=$SSH_DIR
CLAUDE_CREDENTIALS=${CLAUDE_CREDENTIALS_FILE:-/dev/null}

# Environment
ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENV
AGENT_MODE=$AGENT_MODE

# Credentials
GITHUB_TOKEN=${GITHUB_TOKEN:-}
CLAUDE_CODE_OAUTH_TOKEN=${CLAUDE_CODE_OAUTH_TOKEN:-}
HSP_EXTERNAL_HOSTNAME=${EXTERNAL_HOSTNAME:-}

# Mini-prompt sidecar
MINI_PROMPT_SIDECAR_URL=http://homespun-worker:8080

# Grafana
GRAFANA_PORT=$GRAFANA_PORT
GRAFANA_ADMIN_PASSWORD=${GRAFANA_ADMIN_PASSWORD:-admin}
EOF

log_success "Environment file generated: $ENV_FILE"

# Stop existing containers first
log_info "Stopping any existing containers..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" down 2>/dev/null || true
docker stop "$CONTAINER_NAME" 2>/dev/null || true
docker rm "$CONTAINER_NAME" 2>/dev/null || true

# Build compose command
COMPOSE_CMD="docker compose -f $COMPOSE_FILE --env-file $ENV_FILE"

# Add PLG profile if not disabled
if [ "$NO_PLG" = false ]; then
    COMPOSE_CMD="$COMPOSE_CMD --profile plg"
fi

if [ "$DETACHED" = true ]; then
    log_info "Starting containers in detached mode..."
    eval "$COMPOSE_CMD up -d"

    # Start Tailscale sidecar if auth key is available and not already running
    if [ -n "$TAILSCALE_AUTH_KEY" ]; then
        if ! docker ps --format '{{.Names}}' | grep -q '^homespun-tailscale$'; then
            log_info "Starting Tailscale sidecar..."
            TAILSCALE_AUTH_KEY="$TAILSCALE_AUTH_KEY" TS_HOSTNAME="${TS_HOSTNAME:-homespun-dev}" \
                docker compose -f "$TAILSCALE_COMPOSE_FILE" up -d
        else
            log_info "Tailscale sidecar already running."
        fi
    fi

    echo
    log_success "Containers started successfully!"
    echo
    echo "Access URLs:"
    echo "  Homespun:    http://localhost:$HOST_PORT"
    if [ "$NO_PLG" = false ]; then
        echo "  Grafana:     http://localhost:$GRAFANA_PORT (admin/admin)"
        echo "  Loki:        http://localhost:3100"
    fi
    echo
    echo "Useful commands:"
    echo "  View logs:     $0 --logs"
    echo "  Stop:          $0 --stop"
    echo "  Health check:  curl http://localhost:$HOST_PORT/health"
    echo
else
    log_warn "Starting containers in interactive mode..."
    log_warn "Press Ctrl+C to stop."
    echo
    eval "$COMPOSE_CMD up"
    echo
    log_warn "Containers stopped."
fi
