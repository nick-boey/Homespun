#!/bin/bash
# ============================================================================
# Homespun Mock Mode
# ============================================================================
#
# Runs Homespun with mock services and demo data using dotnet run.
# No external dependencies (GitHub API, Claude API, etc.) are required.
#
# Usage:
#   ./mock.sh              # Run in mock mode
#   ./mock.sh --port 8080  # Run in mock mode on custom port
#
# Options:
#   --port PORT    Override the port from the launch profile
#   -h, --help     Show this help message

set -e

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }

# Default values
PORT=""

show_help() {
    head -16 "$0" | tail -12
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
echo

# Build the dotnet run command
DOTNET_ARGS="--project $PROJECT_ROOT/src/Homespun.Server/Homespun.Server.csproj --launch-profile mock"

if [ -n "$PORT" ]; then
    exec dotnet run $DOTNET_ARGS --urls "http://localhost:$PORT"
else
    exec dotnet run $DOTNET_ARGS
fi
