#!/bin/sh
# Tailscale Sidecar Startup Script
# This script starts tailscaled, configures tailscale serve for Homespun,
# and optionally exposes Grafana if the PLG stack is running.
#
# Environment variables (from docker-compose.yml):
#   TS_AUTHKEY   - Tailscale auth key for authentication
#   TS_STATE_DIR - Directory for Tailscale state persistence
#   TS_USERSPACE - Enable userspace networking (no /dev/net/tun needed)

set -e

echo "Starting Tailscale sidecar..."

# Start tailscaled in userspace mode (background)
# The official tailscale image has tailscaled in PATH
tailscaled --state="${TS_STATE_DIR:-/var/lib/tailscale}/tailscaled.state" --tun=userspace-networking &

# Wait for tailscaled socket to appear (up to 15 seconds)
echo "Waiting for tailscaled to start..."
for i in $(seq 1 15); do
    if tailscale status >/dev/null 2>&1; then
        echo "tailscaled is ready"
        break
    fi
    sleep 1
done

# Verify tailscaled started
if ! tailscale status >/dev/null 2>&1; then
    echo "Error: tailscaled not ready after 15 seconds"
    exit 1
fi

# Authenticate with Tailscale
# TS_AUTHKEY is set via environment variable from docker-compose
if [ -n "$TS_AUTHKEY" ]; then
    echo "Connecting to Tailscale..."
    tailscale up --authkey="$TS_AUTHKEY" --hostname="${HOSTNAME:-homespun}" --accept-routes --reset
    echo "Tailscale connected as ${HOSTNAME:-homespun}"
else
    echo "Warning: TS_AUTHKEY not set, Tailscale will not connect"
fi

# Configure tailscale serve for Homespun
# The depends_on in docker-compose.yml ensures homespun is healthy before we start,
# but we still check to be safe
echo "Configuring Tailscale serve for Homespun..."
for i in $(seq 1 30); do
    if wget -q --spider http://homespun:8080/health 2>/dev/null; then
        break
    fi
    echo "Waiting for Homespun health check... ($i/30)"
    sleep 2
done

echo "Enabling Tailscale HTTPS serve for Homespun..."
tailscale serve --bg --https=443 http://homespun:8080 || true
echo "Tailscale HTTPS proxy enabled on port 443 -> homespun:8080"

# If Grafana is reachable (PLG stack running), expose it too
# Check periodically as Grafana may take time to start
(
    for i in $(seq 1 30); do
        if wget -q --spider http://homespun-grafana:3000/api/health 2>/dev/null; then
            echo "Enabling Tailscale HTTPS serve for Grafana..."
            tailscale serve --bg --https=3000 http://homespun-grafana:3000 || true
            echo "Tailscale HTTPS proxy enabled on port 3000 -> homespun-grafana:3000"
            break
        fi
        sleep 2
    done
) &

# Show Tailscale status
tailscale status || true

# Keep container running
echo "Tailscale sidecar running. Press Ctrl+C to stop."
wait
