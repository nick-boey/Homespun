#!/bin/bash
set -e

# ============================================================================
# Homespun Container Builder
# ============================================================================
#
# Builds all Homespun container images locally.
#
# Usage:
#   ./build-containers.sh              # Build all images
#   ./build-containers.sh --no-base    # Skip base image (use cached)
#   ./build-containers.sh --only base  # Build only the base image
#   ./build-containers.sh --only web   # Build only the web image
#   ./build-containers.sh --debug      # Build server in Debug configuration
#
# Images built:
#   homespun-base:local    - Shared base image (tooling)
#   homespun:local         - Main server application
#   homespun-worker:local  - Worker sidecar
#   homespun-web:local     - React frontend (nginx)

# Get script directory and repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default values
BUILD_BASE=true
BUILD_SERVER=true
BUILD_WORKER=true
BUILD_WEB=true
BUILD_CONFIG="Release"

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

show_help() {
    head -18 "$0" | tail -14
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --no-base) BUILD_BASE=false ;;
        --debug) BUILD_CONFIG="Debug" ;;
        --only)
            BUILD_BASE=false
            BUILD_SERVER=false
            BUILD_WORKER=false
            BUILD_WEB=false
            case $2 in
                base) BUILD_BASE=true ;;
                server|homespun) BUILD_SERVER=true; BUILD_BASE=true ;;
                worker) BUILD_WORKER=true; BUILD_BASE=true ;;
                web) BUILD_WEB=true ;;
                *) log_error "Unknown target: $2"; exit 1 ;;
            esac
            shift
            ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

cd "$REPO_ROOT"

echo
log_info "=== Homespun Container Builder ==="
echo

# Check Docker
if ! docker version >/dev/null 2>&1; then
    log_error "Docker is not running. Please start Docker and try again."
    exit 1
fi

BASE_IMAGE="homespun-base:local"
SERVER_IMAGE="homespun:local"
WORKER_IMAGE="homespun-worker:local"
WEB_IMAGE="homespun-web:local"

# Track timing
START_TIME=$SECONDS

# Build base image
if [ "$BUILD_BASE" = true ]; then
    log_info "[base] Building base tooling image..."
    if ! DOCKER_BUILDKIT=1 docker build -t "$BASE_IMAGE" -f Dockerfile.base .; then
        log_error "[base] Failed to build base image."
        exit 1
    fi
    log_success "[base] Built: $BASE_IMAGE"
    echo
fi

# Build server image
if [ "$BUILD_SERVER" = true ]; then
    log_info "[server] Building main Homespun image ($BUILD_CONFIG)..."
    if ! docker build -t "$SERVER_IMAGE" --build-arg BUILD_CONFIGURATION="$BUILD_CONFIG" .; then
        log_error "[server] Failed to build server image."
        exit 1
    fi
    log_success "[server] Built: $SERVER_IMAGE"
    echo
fi

# Build worker image
if [ "$BUILD_WORKER" = true ]; then
    log_info "[worker] Building worker image..."
    if ! DOCKER_BUILDKIT=1 docker build -t "$WORKER_IMAGE" -f src/Homespun.Worker/Dockerfile src/Homespun.Worker; then
        log_error "[worker] Failed to build worker image."
        exit 1
    fi
    log_success "[worker] Built: $WORKER_IMAGE"
    echo
fi

# Build web image
if [ "$BUILD_WEB" = true ]; then
    log_info "[web] Building web frontend image..."
    if ! docker build -t "$WEB_IMAGE" -f src/Homespun.Web/Dockerfile src/Homespun.Web; then
        log_error "[web] Failed to build web image."
        exit 1
    fi
    log_success "[web] Built: $WEB_IMAGE"
    echo
fi

ELAPSED=$(( SECONDS - START_TIME ))
log_success "=== All images built successfully in ${ELAPSED}s ==="
echo
log_info "Images:"
[ "$BUILD_BASE" = true ] && echo "  $BASE_IMAGE"
[ "$BUILD_SERVER" = true ] && echo "  $SERVER_IMAGE"
[ "$BUILD_WORKER" = true ] && echo "  $WORKER_IMAGE"
[ "$BUILD_WEB" = true ] && echo "  $WEB_IMAGE"
echo
