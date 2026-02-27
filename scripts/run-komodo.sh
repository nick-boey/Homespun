#!/bin/bash
set -e

# ============================================================================
# Komodo Runtime Script
# ============================================================================
#
# This script starts/stops the Komodo container management system.
#
# Usage:
#   ./scripts/run-komodo.sh              # Start Komodo
#   ./scripts/run-komodo.sh --stop       # Stop Komodo
#   ./scripts/run-komodo.sh --logs       # View logs
#   ./scripts/run-komodo.sh --status     # Check status
#
# Options:
#   --stop          Stop all Komodo containers
#   --logs          Follow Komodo logs
#   --status        Show container status
#   -it             Run in foreground (interactive mode)
#   -d, --detach    Run in background (default)
#
# Prerequisites:
#   - Run ./scripts/install-komodo.sh first
#   - Tailscale auth key configured for VPN access

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

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --stop) ACTION="stop" ;;
        --logs) ACTION="logs" ;;
        --status) ACTION="status" ;;
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
        docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" down
        log_success "Komodo stopped."
        exit 0
        ;;
    logs)
        log_info "Following Komodo logs (Ctrl+C to exit)..."
        docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" logs -f
        exit 0
        ;;
    status)
        log_info "Komodo container status:"
        echo
        docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" ps
        exit 0
        ;;
esac

# Start Komodo
log_info "[1/3] Validating Docker..."
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running."
    exit 1
fi
log_success "      Docker is available."

log_info "[2/3] Checking configuration..."
log_success "      Compose file: $COMPOSE_FILE"
log_success "      Environment:  $ENV_FILE"

log_info "[3/3] Starting Komodo..."

# Build compose command
COMPOSE_CMD="docker compose -f $COMPOSE_FILE --env-file $ENV_FILE"

if [ "$DETACHED" = true ]; then
    eval "$COMPOSE_CMD up -d"

    echo
    log_success "Komodo started successfully!"
    echo
    echo "Access URLs:"
    echo "  Komodo UI:    https://<tailscale-hostname>:3500"
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
