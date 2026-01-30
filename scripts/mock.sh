#!/bin/bash
# ============================================================================
# Homespun Mock Mode (Container)
# ============================================================================
#
# Builds and runs Homespun in a container with mock mode enabled.
# Uses seeded demo data - no external dependencies required
# (GitHub API, Claude API, etc.)
#
# Usage:
#   ./mock.sh              # Run in mock mode on default port (5095)
#   ./mock.sh --port 8080  # Run in mock mode on custom port
#   ./mock.sh -it          # Run in interactive mode (foreground)
#   ./mock.sh --stop       # Stop the mock container
#   ./mock.sh --logs       # View container logs
#
# Options:
#   --port PORT            Override host port (default: 5095)
#   -it, --interactive     Run in interactive mode (foreground)
#   -d, --detach           Run in detached mode (background) [default]
#   --stop                 Stop the mock container
#   --logs                 Follow container logs
#   --container-name NAME  Override container name (default: homespun-mock)
#
# The application runs at: http://localhost:5095 (or custom port)

set -e

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Default values
HOST_PORT="5095"
CONTAINER_NAME="homespun-mock"
RUN_ARGS=""

# Colors
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }

show_help() {
    head -25 "$0" | tail -20
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --port) HOST_PORT="$2"; shift ;;
        -it|--interactive) RUN_ARGS="$RUN_ARGS -it" ;;
        -d|--detach) RUN_ARGS="$RUN_ARGS -d" ;;
        --stop) RUN_ARGS="--stop"; break ;;
        --logs) RUN_ARGS="--logs"; break ;;
        --container-name) CONTAINER_NAME="$2"; shift ;;
        -h|--help) show_help ;;
        *) echo "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

log_info "=== Homespun Mock Mode (Container) ==="
log_info "Building and running with mock services and demo data..."
echo

# Call run.sh with --local --mock flags and pass through additional args
exec "$SCRIPT_DIR/run.sh" \
    --local \
    --mock \
    --port "$HOST_PORT" \
    --container-name "$CONTAINER_NAME" \
    $RUN_ARGS
