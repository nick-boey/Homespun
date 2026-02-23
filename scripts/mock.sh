#!/bin/bash
# ============================================================================
# Homespun Mock Mode
# ============================================================================
#
# Runs Homespun with mock services and demo data using dotnet run.
# No external dependencies (GitHub API, Claude API, etc.) are required.
#
# Usage:
#   ./mock.sh              # Run in mock mode (foreground)
#   ./mock.sh &            # Run in mock mode (background)
#   ./mock.sh --port 8080  # Run in mock mode on custom port
#
# Options:
#   --port PORT    Override the port from the launch profile
#   -h, --help     Show this help message
#
# WARNING: This script runs a long-lived server process. If running as a
# background shell in Claude Code, do NOT use KillShell on this process.
# Killing this shell may terminate your entire session. Instead, use
# pkill or kill with the dotnet process PID directly.

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

show_help() {
    head -20 "$0" | tail -16
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --port) PORT="$2"; shift ;;
        -h|--help) show_help ;;
        *) echo "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

log_info "=== Homespun Mock Mode ==="
log_info "Running with mock services and demo data..."
log_warn "WARNING: Do not use KillShell on this process - use 'pkill -f dotnet.*mock' instead"
echo

# Build the dotnet run command
DOTNET_ARGS="--project $PROJECT_ROOT/src/Homespun.Server/Homespun.Server.csproj --launch-profile mock"

# Use exec to replace the shell with dotnet, making the process tree cleaner
# The dotnet process will have its own PID and can be managed directly
if [ -n "$PORT" ]; then
    exec dotnet run $DOTNET_ARGS --urls "http://localhost:$PORT"
else
    exec dotnet run $DOTNET_ARGS
fi
