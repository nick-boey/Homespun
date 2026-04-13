#!/bin/bash
set -e

# ============================================================================
# Komodo Runtime Script
# ============================================================================
#
# This script starts/stops the Komodo container management system.
#
# Usage:
#   ./scripts/run-komodo.sh              # Start Komodo with Tailscale
#   ./scripts/run-komodo.sh --stop       # Stop Komodo
#   ./scripts/run-komodo.sh --logs       # View logs
#   ./scripts/run-komodo.sh --status     # Check status
#   ./scripts/run-komodo.sh --no-tailscale  # Start without Tailscale
#
# Options:
#   --stop            Stop all Komodo containers
#   --logs            Follow Komodo logs
#   --status          Show container status
#   --no-tailscale    Disable Tailscale sidecar
#   -it               Run in foreground (interactive mode)
#   -d, --detach      Run in background (default)
#
# Configuration:
#   Reads TAILSCALE_AUTH_KEY from .env at the repo root
#   (copy .env.example to .env).
#
# Prerequisites:
#   - Run ./scripts/install-komodo.sh first

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

# Get script directory and repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Configuration
KOMODO_DIR="/etc/komodo"
COMPOSE_FILE="$KOMODO_DIR/komodo.compose.yml"
ENV_FILE="$KOMODO_DIR/compose.env"
TAILSCALE_COMPOSE_FILE="$REPO_ROOT/config/tailscale/compose.yml"

# Default values
ACTION="start"
DETACHED=true
NO_TAILSCALE=false

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --stop) ACTION="stop" ;;
        --logs) ACTION="logs" ;;
        --status) ACTION="status" ;;
        --no-tailscale) NO_TAILSCALE=true ;;
        -it|--interactive) DETACHED=false ;;
        -d|--detach) DETACHED=true ;;
        -h|--help)
            head -30 "$0" | tail -25
            exit 0
            ;;
        *) log_error "Unknown parameter: $1"; exit 1 ;;
    esac
    shift
done

echo
log_info "=== Komodo Container Manager ==="
echo

# Validate installation
if [ ! -f "$COMPOSE_FILE" ]; then
    log_error "Komodo is not installed."
    log_error "Run: ./scripts/install-komodo.sh"
    exit 1
fi

if [ ! -f "$ENV_FILE" ]; then
    log_error "Komodo configuration not found."
    log_error "Run: ./scripts/install-komodo.sh"
    exit 1
fi

# Handle actions
case "$ACTION" in
    stop)
        log_info "Stopping Komodo containers..."
        sudo docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" down
        docker compose -f "$TAILSCALE_COMPOSE_FILE" down 2>/dev/null || true
        log_success "Komodo stopped."
        exit 0
        ;;
    logs)
        log_info "Following Komodo logs (Ctrl+C to exit)..."
        sudo docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" logs -f
        exit 0
        ;;
    status)
        log_info "Komodo container status:"
        echo
        sudo docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" ps
        exit 0
        ;;
esac

# Start Komodo
log_info "[1/4] Validating Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running."
    exit 1
fi
log_success "      Docker is available."

# Ensure shared network exists (external: true in compose files)
docker network create homespun-net 2>/dev/null || true

log_info "[2/4] Checking configuration..."
log_success "      Compose file: $COMPOSE_FILE"
log_success "      Environment:  $ENV_FILE"

# Step 3: Read Tailscale credentials
log_info "[3/4] Reading Tailscale credentials..."

if [ "$NO_TAILSCALE" = true ]; then
    TAILSCALE_AUTH_KEY=""
    log_info "      Tailscale disabled (--no-tailscale flag)"
else
    # Source .env at repo root if it exists
    DOTENV_FILE="$REPO_ROOT/.env"
    if [ -f "$DOTENV_FILE" ]; then
        set -a
        # shellcheck disable=SC1090
        source "$DOTENV_FILE"
        set +a
    fi

    if [ -z "${TAILSCALE_AUTH_KEY:-}" ]; then
        log_warn "      Tailscale auth key not found (Tailscale will be disabled)."
        log_warn "      Set TAILSCALE_AUTH_KEY in .env for VPN access."
    else
        # Check if the shared Tailscale sidecar is already running
        if docker ps --format '{{.Names}}' | grep -q '^homespun-tailscale$'; then
            log_warn "      Tailscale sidecar already running."
            log_warn "      Skipping Tailscale launch."
            TAILSCALE_AUTH_KEY=""
        else
            MASKED_TS_KEY="${TAILSCALE_AUTH_KEY:0:15}..."
            log_success "      Tailscale auth key found: $MASKED_TS_KEY"
        fi
    fi
fi

log_info "[4/4] Starting Komodo..."

# Build compose command (sudo required: compose.env is root-owned)
COMPOSE_CMD="sudo docker compose -f $COMPOSE_FILE --env-file $ENV_FILE"

echo
log_info "======================================"
log_info "  Komodo Configuration"
log_info "======================================"
echo "  Core API:     http://localhost:9120 (local only)"
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    echo "  Tailscale:    Enabled (Komodo at https://<tailscale-hostname>:3500)"
else
    echo "  Tailscale:    Disabled"
fi
log_info "======================================"
echo

if [ "$DETACHED" = true ]; then
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

    # Push host-specific Komodo Variables so stack redeploys from the UI can
    # resolve [[HOMESPUN_HOME]], [[HOST_UID]], [[HOST_GID]], [[DOCKER_GID]].
    # Non-fatal: if Core isn't healthy in time, the sync script logs manual-
    # repair steps and exits 0 without blocking Komodo from running.
    SYNC_SCRIPT="$SCRIPT_DIR/sync-komodo-vars.sh"
    if [ -x "$SYNC_SCRIPT" ]; then
        log_info "Syncing host-specific Komodo Variables..."
        "$SYNC_SCRIPT" || log_warn "Variable sync returned non-zero (continuing)."
    else
        log_warn "$SYNC_SCRIPT not found or not executable; skipping variable sync."
    fi

    echo
    log_success "Komodo started successfully!"
    echo
    echo "Access URLs:"
    if [ -n "$TAILSCALE_AUTH_KEY" ]; then
        echo "  Komodo UI:    https://<tailscale-hostname>:3500"
    fi
    echo "  Core API:     http://localhost:9120 (local only)"
    echo
    echo "Useful commands:"
    echo "  View logs:    $0 --logs"
    echo "  Stop:         $0 --stop"
    echo "  Status:       $0 --status"
    echo
else
    log_warn "Starting Komodo in foreground mode..."
    log_warn "Press Ctrl+C to stop."
    echo
    eval "$COMPOSE_CMD up"
fi
