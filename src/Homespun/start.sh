#!/bin/bash
set -e

# Tailscale networking is handled by a sidecar container (see docker-compose.yml)
# This script simply starts the Homespun application
#
# Note: The Dockerfile pre-creates /home/homespun with world-writable permissions
# to support docker-compose user override (HOST_UID/HOST_GID) for proper volume ownership

echo "Starting Homespun..."

# Build command line arguments
ARGS=""

# Check for test agent auto-start
if [ -n "$START_TEST_AGENT_PROJECT" ]; then
    echo "Test agent auto-start enabled for project: $START_TEST_AGENT_PROJECT"
    ARGS="$ARGS --start-test-agent=$START_TEST_AGENT_PROJECT"
fi

exec dotnet Homespun.dll $ARGS
