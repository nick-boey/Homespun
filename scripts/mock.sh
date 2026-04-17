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
#   ./mock.sh --with-worker    # Bring up docker-compose `worker` and point the
#                              # backend at it via AgentExecution:Mode=SingleContainer.
#                              # Dev-only; single active session at a time (see
#                              # docs/session-events.md and design.md). Requires
#                              # CLAUDE_CODE_OAUTH_TOKEN to be set.
#
# Options:
#   --port PORT      Override the backend port (default: 5101)
#   --foreground     Run backend only in foreground with output to terminal
#   --with-worker    Start docker-compose worker and enable SingleContainer mode
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
WITH_WORKER=false
WORKER_HOST_PORT="${WORKER_HOST_PORT:-8081}"

show_help() {
    head -30 "$0" | tail -26
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --port) PORT="$2"; shift ;;
        --foreground) FOREGROUND=true ;;
        --with-worker) WITH_WORKER=true ;;
        -h|--help) show_help ;;
        *) echo "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

# --with-worker: validate env, start compose worker, wait for health, export agent-mode.
if [ "$WITH_WORKER" = true ]; then
    if [ -z "$CLAUDE_CODE_OAUTH_TOKEN" ]; then
        echo -e "\033[0;31mERROR: CLAUDE_CODE_OAUTH_TOKEN is not set; aborting.\033[0m" >&2
        echo -e "\033[0;31m       --with-worker boots a real Claude Agent SDK worker which requires authentication.\033[0m" >&2
        exit 1
    fi

    log_info "Starting docker-compose worker on host port ${WORKER_HOST_PORT}..."
    (cd "$PROJECT_ROOT" && WORKER_HOST_PORT="${WORKER_HOST_PORT}" docker compose up -d worker)

    log_info "Waiting for worker /api/health on http://localhost:${WORKER_HOST_PORT}..."
    WORKER_READY=false
    for _ in $(seq 1 30); do
        if curl -sf "http://localhost:${WORKER_HOST_PORT}/api/health" >/dev/null 2>&1; then
            WORKER_READY=true
            break
        fi
        sleep 1
    done
    if [ "$WORKER_READY" != true ]; then
        log_warn "Worker did not become healthy within 30s — aborting."
        (cd "$PROJECT_ROOT" && docker compose stop worker) || true
        exit 1
    fi
    log_success "Worker is healthy."

    export AgentExecution__Mode="SingleContainer"
    export AgentExecution__SingleContainer__WorkerUrl="http://localhost:${WORKER_HOST_PORT}"
    # SingleContainer mode is gated on IsDevelopment() — override the Mock launch profile's env.
    export ASPNETCORE_ENVIRONMENT="Development"
    # The `mock` launch profile enables HOMESPUN_MOCK_MODE which bypasses the
    # real agent-execution registration entirely. Force mock mode off so the
    # SingleContainer shim actually takes effect.
    export HOMESPUN_MOCK_MODE="false"
    export MockMode__Enabled="false"

fi

# Compose worker teardown helper — called from cleanup() below and from the
# foreground-mode trap when --with-worker is set.
worker_teardown() {
    if [ "$WITH_WORKER" = true ]; then
        log_info "Stopping docker-compose worker..."
        (cd "$PROJECT_ROOT" && docker compose stop worker) || true
    fi
}

# Build the dotnet run command args
DOTNET_ARGS="--project $PROJECT_ROOT/src/Homespun.Server/Homespun.Server.csproj --launch-profile mock"

if [ -n "$PORT" ]; then
    DOTNET_ARGS="$DOTNET_ARGS --urls http://localhost:$PORT"
fi

# Foreground mode: original behavior (backend only, output to terminal)
if [ "$FOREGROUND" = true ]; then
    trap worker_teardown EXIT INT TERM
    log_info "=== Homespun Mock Mode (foreground) ==="
    log_info "Running backend only with output to terminal..."
    log_warn "WARNING: Do not use KillShell on this process - use 'pkill -f dotnet.*mock' instead"
    echo
    dotnet run $DOTNET_ARGS
    exit $?
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
    worker_teardown
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
while kill -0 "$BACKEND_PID" 2>/dev/null && kill -0 "$FRONTEND_PID" 2>/dev/null; do
    sleep 1
done

# Determine which process exited
if kill -0 "$BACKEND_PID" 2>/dev/null; then
    wait "$FRONTEND_PID" 2>/dev/null || true
    log_warn "Frontend process exited. Stopping backend..."
else
    wait "$BACKEND_PID" 2>/dev/null || true
    log_warn "Backend process exited. Stopping frontend..."
fi
