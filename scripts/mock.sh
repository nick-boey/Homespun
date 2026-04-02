#!/bin/bash
# ============================================================================
# Homespun Mock Mode
# ============================================================================
#
# Runs Homespun with mock services and demo data.
# Starts both the backend (dotnet) and frontend (Vite) dev servers.
# Logs are captured to logs/mock-backend.log and logs/mock-frontend.log.
#
# Usage:
#   ./mock.sh                  # Run backend + frontend (background, logs to files)
#   ./mock.sh --foreground     # Run backend only in foreground (original behavior)
#   ./mock.sh --port 8080      # Run on custom backend port
#
# Options:
#   --port PORT      Override the backend port (default: 5101)
#   --foreground     Run backend only in foreground with output to terminal
#   -h, --help       Show this help message
#
# WARNING: This script runs long-lived server processes. If running as a
# background shell in Claude Code, do NOT use KillShell on this process.
# Killing this shell may terminate your entire session. Instead, use
# pkill or kill with the process PIDs directly.

set -e

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }

# Default values
PORT=""
FOREGROUND=false

show_help() {
    head -24 "$0" | tail -20
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --port) PORT="$2"; shift ;;
        --foreground) FOREGROUND=true ;;
        -h|--help) show_help ;;
        *) echo "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# Build the dotnet run command args
DOTNET_ARGS="--project $PROJECT_ROOT/src/Homespun.Server/Homespun.Server.csproj --launch-profile mock"

if [ -n "$PORT" ]; then
    DOTNET_ARGS="$DOTNET_ARGS --urls http://localhost:$PORT"
fi

# Foreground mode: original behavior (backend only, output to terminal)
if [ "$FOREGROUND" = true ]; then
    log_info "=== Homespun Mock Mode (foreground) ==="
    log_info "Running backend only with output to terminal..."
    log_warn "WARNING: Do not use KillShell on this process - use 'pkill -f dotnet.*mock' instead"
    echo
    exec dotnet run $DOTNET_ARGS
fi

# Background mode: run both backend and frontend with logs captured to files
log_info "=== Homespun Mock Mode ==="
log_info "Starting backend and frontend servers..."
log_warn "WARNING: Do not use KillShell on this process - use 'pkill -f dotnet.*mock' instead"
echo

# Set up log directory
LOG_DIR="$PROJECT_ROOT/logs"
mkdir -p "$LOG_DIR"

# Cleanup function to kill child processes
BACKEND_PID=""
FRONTEND_PID=""

cleanup() {
    log_info "Shutting down servers..."
    if [ -n "$BACKEND_PID" ]; then
        kill "$BACKEND_PID" 2>/dev/null || true
    fi
    if [ -n "$FRONTEND_PID" ]; then
        kill "$FRONTEND_PID" 2>/dev/null || true
    fi
    wait "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
    log_info "Servers stopped."
}
trap cleanup EXIT INT TERM

# Start backend
log_info "Starting backend server..."
dotnet run $DOTNET_ARGS > "$LOG_DIR/mock-backend.log" 2>&1 &
BACKEND_PID=$!
log_success "Backend started (PID: $BACKEND_PID)"

# Start frontend
log_info "Starting frontend dev server..."
cd "$PROJECT_ROOT/src/Homespun.Web"
npm run dev > "$LOG_DIR/mock-frontend.log" 2>&1 &
FRONTEND_PID=$!
cd "$PROJECT_ROOT"
log_success "Frontend started (PID: $FRONTEND_PID)"

echo
log_success "Both servers are running!"
log_info "Backend log:  $LOG_DIR/mock-backend.log"
log_info "Frontend log: $LOG_DIR/mock-frontend.log"
echo
log_info "Use 'tail -f $LOG_DIR/mock-backend.log' to follow backend logs"
log_info "Use 'tail -f $LOG_DIR/mock-frontend.log' to follow frontend logs"
echo
log_warn "Press Ctrl+C to stop both servers"

# Wait for either process to exit
wait -n "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
EXIT_CODE=$?

# If one process exited, log which one and wait for cleanup
if kill -0 "$BACKEND_PID" 2>/dev/null; then
    log_warn "Frontend process exited (code: $EXIT_CODE). Stopping backend..."
else
    log_warn "Backend process exited (code: $EXIT_CODE). Stopping frontend..."
fi

exit $EXIT_CODE
