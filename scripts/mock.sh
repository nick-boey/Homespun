#!/bin/bash
# ============================================================================
# Homespun Mock Mode (Container)
# ============================================================================
#
# Builds and runs Homespun in a container with mock mode enabled.
# Uses seeded demo data - no external dependencies required
# (GitHub API, Claude API, etc.)
#
# Each git worktree automatically gets its own container, port, and data
# directory based on a hash of the worktree path. This allows multiple
# agents to run mock mode concurrently without conflicts.
#
# Usage:
#   ./mock.sh              # Run in mock mode (auto-configured per worktree)
#   ./mock.sh --port 8080  # Run in mock mode on custom port
#   ./mock.sh -it          # Run in interactive mode (foreground)
#   ./mock.sh --stop       # Stop the mock container for this worktree
#   ./mock.sh --logs       # View container logs
#
# Options:
#   --port PORT            Override host port (default: auto from worktree)
#   -it, --interactive     Run in interactive mode (foreground)
#   -d, --detach           Run in detached mode (background) [default]
#   --stop                 Stop the mock container
#   --logs                 Follow container logs
#   --container-name NAME  Override container name (default: auto from worktree)
#   --data-dir DIR         Override data directory (default: auto from worktree)
#
# Worktree Isolation:
#   Container name: homespun-mock-<hash>
#   Port: 15000-15999 (computed from worktree path)
#   Data dir: ~/.homespun-container/mock-<hash>

set -e

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }

# ============================================================================
# Worktree Detection and Auto-Configuration
# ============================================================================

# Get the git worktree root path
get_worktree_root() {
    git rev-parse --show-toplevel 2>/dev/null || echo "$SCRIPT_DIR/.."
}

# Generate an 8-character hash from a path
get_worktree_hash() {
    local path="$1"
    # Use md5sum or md5 depending on platform
    if command -v md5sum &>/dev/null; then
        echo -n "$path" | md5sum | cut -c1-8
    elif command -v md5 &>/dev/null; then
        echo -n "$path" | md5 | cut -c1-8
    else
        # Fallback: simple hash using cksum
        echo -n "$path" | cksum | cut -d' ' -f1 | head -c8
    fi
}

# Compute port from hash (base 15000, range 1000)
compute_port_from_hash() {
    local hash="$1"
    # Convert first 4 hex chars to decimal and mod 1000
    local hex_val="${hash:0:4}"
    local decimal_val=$((16#$hex_val))
    local port=$((15000 + (decimal_val % 1000)))
    echo "$port"
}

# Check if a port is available
is_port_available() {
    local port="$1"
    # Use ss, netstat, or lsof to check port
    if command -v ss &>/dev/null; then
        ! ss -tuln 2>/dev/null | grep -q ":$port "
    elif command -v netstat &>/dev/null; then
        ! netstat -tuln 2>/dev/null | grep -q ":$port "
    elif command -v lsof &>/dev/null; then
        ! lsof -i ":$port" &>/dev/null
    else
        # Assume available if we can't check
        return 0
    fi
}

# Find an available port starting from a computed value
find_available_port() {
    local start_port="$1"
    local max_attempts=50
    local port=$start_port

    for ((i=0; i<max_attempts; i++)); do
        if is_port_available "$port"; then
            echo "$port"
            return 0
        fi
        port=$((port + 1))
        # Wrap around within range
        if [ $port -ge 16000 ]; then
            port=15000
        fi
    done

    # Return start port if nothing found (let docker fail with clear error)
    echo "$start_port"
}

# Get worktree identifier (hash)
get_worktree_identifier() {
    local worktree_root
    worktree_root="$(get_worktree_root)"
    get_worktree_hash "$worktree_root"
}

# Get auto-configured port for this worktree
get_worktree_port() {
    local hash="$1"
    local computed_port
    computed_port="$(compute_port_from_hash "$hash")"
    find_available_port "$computed_port"
}

# ============================================================================
# Main Script
# ============================================================================

# Generate worktree-specific defaults
WORKTREE_ID="$(get_worktree_identifier)"
WORKTREE_ROOT="$(get_worktree_root)"

# Default values (auto-configured from worktree)
HOST_PORT=""
HOST_PORT_OVERRIDE=""
CONTAINER_NAME=""
CONTAINER_NAME_OVERRIDE=""
DATA_DIR=""
DATA_DIR_OVERRIDE=""
RUN_ARGS=""

show_help() {
    head -35 "$0" | tail -30
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --port) HOST_PORT_OVERRIDE="$2"; shift ;;
        -it|--interactive) RUN_ARGS="$RUN_ARGS -it" ;;
        -d|--detach) RUN_ARGS="$RUN_ARGS -d" ;;
        --stop) RUN_ARGS="--stop"; break ;;
        --logs) RUN_ARGS="--logs"; break ;;
        --container-name) CONTAINER_NAME_OVERRIDE="$2"; shift ;;
        --data-dir) DATA_DIR_OVERRIDE="$2"; shift ;;
        -h|--help) show_help ;;
        *) echo "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# Apply overrides or use auto-configured values
if [ -n "$HOST_PORT_OVERRIDE" ]; then
    HOST_PORT="$HOST_PORT_OVERRIDE"
else
    HOST_PORT="$(get_worktree_port "$WORKTREE_ID")"
fi

if [ -n "$CONTAINER_NAME_OVERRIDE" ]; then
    CONTAINER_NAME="$CONTAINER_NAME_OVERRIDE"
else
    CONTAINER_NAME="homespun-mock-$WORKTREE_ID"
fi

if [ -n "$DATA_DIR_OVERRIDE" ]; then
    DATA_DIR="$DATA_DIR_OVERRIDE"
else
    DATA_DIR="$HOME/.homespun-container/mock-$WORKTREE_ID"
fi

log_info "=== Homespun Mock Mode (Container) ==="
log_info "Building and running with mock services and demo data..."
echo
log_info "Worktree Configuration:"
echo "  Worktree:    $WORKTREE_ROOT"
echo "  Identifier:  $WORKTREE_ID"
echo "  Container:   $CONTAINER_NAME"
echo "  Port:        $HOST_PORT"
echo "  Data dir:    $DATA_DIR"
echo

# Call run.sh with --local --mock flags and pass through additional args
exec "$SCRIPT_DIR/run.sh" \
    --local \
    --mock \
    --port "$HOST_PORT" \
    --container-name "$CONTAINER_NAME" \
    --data-dir "$DATA_DIR" \
    $RUN_ARGS
