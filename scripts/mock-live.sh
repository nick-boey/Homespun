#!/bin/bash
# ============================================================================
# Homespun Mock Mode with Live Claude Sessions
# ============================================================================
#
# Starts Homespun in mock mode with live Claude Code sessions.
# This mode uses mock services for GitHub, Git, etc., but uses real Claude
# sessions targeting a test workspace directory. This is useful for testing
# the AskUserQuestion tool and other Claude interactions without needing
# a full GitHub integration.
#
# Usage:
#   ./mock-live.sh                           # Start with default test-workspace
#   ./mock-live.sh /path/to/custom/folder    # Start with custom working directory
#
# The application runs at: http://localhost:5095

set -e

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }

log_info "=== Homespun Mock Mode with Live Claude Sessions ==="
echo

# Custom working directory from first argument
if [ -n "$1" ]; then
    export MockMode__LiveClaudeSessionsWorkingDirectory="$1"
    log_info "Using custom working directory: $1"
else
    DEFAULT_DIR="$PROJECT_DIR/test-workspace"
    log_info "Using default working directory: $DEFAULT_DIR"
fi

log_info "Starting with mock services + live Claude sessions..."
log_success "Claude sessions will target the test workspace"
echo

exec dotnet run --project "$PROJECT_DIR/src/Homespun" --launch-profile mock-live
