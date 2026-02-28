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
# Environment Variables:
#   HSP_TAILSCALE_AUTH_KEY    Tailscale auth key (preferred for VM secrets)
#   TAILSCALE_AUTH_KEY        Tailscale auth key (fallback)
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
        sudo docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" --profile tailscale down
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

log_info "[2/4] Checking configuration..."
log_success "      Compose file: $COMPOSE_FILE"
log_success "      Environment:  $ENV_FILE"

# Step 3: Read Tailscale credentials
log_info "[3/4] Reading Tailscale credentials..."

if [ "$NO_TAILSCALE" = true ]; then
    TAILSCALE_AUTH_KEY=""
    log_info "      Tailscale disabled (--no-tailscale flag)"
else
    # Source ~/.homespun/env if it exists
    HOMESPUN_ENV_FILE="$HOME/.homespun/env"
    if [ -f "$HOMESPUN_ENV_FILE" ]; then
        source "$HOMESPUN_ENV_FILE"
    fi

    # 1. HSP_TAILSCALE_AUTH_KEY (for VM secrets)
    # 2. TAILSCALE_AUTH_KEY (standard)
    TAILSCALE_AUTH_KEY="${HSP_TAILSCALE_AUTH_KEY:-${TAILSCALE_AUTH_KEY:-}}"

    # Try reading from .env file in repo root
    if [ -z "$TAILSCALE_AUTH_KEY" ] && [ -f "$REPO_ROOT/.env" ]; then
        TAILSCALE_AUTH_KEY=$(grep -E "^TAILSCALE_AUTH_KEY=" "$REPO_ROOT/.env" 2>/dev/null | cut -d'=' -f2- | tr -d '"' | tr -d "'" || true)
    fi

    if [ -z "$TAILSCALE_AUTH_KEY" ]; then
        log_warn "      Tailscale auth key not found (Tailscale will be disabled)."
        log_warn "      Set TAILSCALE_AUTH_KEY in ~/.homespun/env for VPN access."
    else
        # Check if Homespun's Tailscale sidecar is already running
        if docker ps --format '{{.Names}}' | grep -q '^homespun-tailscale$'; then
            log_warn "      Tailscale container (homespun-tailscale) is already running."
            log_warn "      Skipping Tailscale sidecar to avoid conflict."
            log_warn "      The existing sidecar will serve Komodo if it's on the same network."
            TAILSCALE_AUTH_KEY=""
        else
            MASKED_TS_KEY="${TAILSCALE_AUTH_KEY:0:15}..."
            log_success "      Tailscale auth key found: $MASKED_TS_KEY"
        fi
    fi
fi

log_info "[4/4] Starting Komodo..."

# Build compose command (sudo required: compose.env is root-owned)
# Pass Tailscale env vars through sudo if available
SUDO_ENV_ARGS=""
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    SUDO_ENV_ARGS="TAILSCALE_AUTH_KEY=$TAILSCALE_AUTH_KEY TAILSCALE_CONFIG_DIR=$REPO_ROOT/config/tailscale"
fi

COMPOSE_CMD="sudo $SUDO_ENV_ARGS docker compose -f $COMPOSE_FILE --env-file $ENV_FILE"

# Add Tailscale profile if auth key is provided
if [ -n "$TAILSCALE_AUTH_KEY" ]; then
    COMPOSE_CMD="$COMPOSE_CMD --profile tailscale"
fi

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
