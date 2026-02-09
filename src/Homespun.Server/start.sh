#!/bin/bash
set -e

# Homespun Server Container Startup Script
# This script handles:
# 1. Tailscale setup (if TS_AUTHKEY is provided)
# 2. Git configuration
# 3. Starting the Homespun Server application

# Ensure HOME is set correctly (Windows Docker may pass incorrect HOME)
if [ "$(id -u)" = "0" ]; then
    export HOME=/root
else
    export HOME=/home/homespun
fi

# Configure git to trust mounted directories (avoids "dubious ownership" errors)
git config --global --add safe.directory '*' 2>/dev/null || true

# Configure git for the container
git config --global core.askpass /data/git-askpass.sh 2>/dev/null || true
git config --global credential.helper '' 2>/dev/null || true
git config --global user.name "${GIT_AUTHOR_NAME:-Homespun Bot}" 2>/dev/null || true
git config --global user.email "${GIT_AUTHOR_EMAIL:-homespun@localhost}" 2>/dev/null || true

# Start Tailscale if auth key is provided
TS_AUTHKEY="${TAILSCALE_AUTH_KEY:-$TS_AUTHKEY}"
if [ -n "$TS_AUTHKEY" ]; then
    echo "Starting Tailscale..."

    if [ -d "/data" ]; then
        TS_STATE_DIR="${TS_STATE_DIR:-/data/tailscale}"
    else
        TS_STATE_DIR="${TS_STATE_DIR:-/tmp/tailscale-state}"
    fi
    TS_SOCKET_DIR="/tmp/tailscale"
    TS_SOCKET="$TS_SOCKET_DIR/tailscaled.sock"

    mkdir -p "$TS_STATE_DIR" "$TS_SOCKET_DIR"

    tailscaled --state="$TS_STATE_DIR/tailscaled.state" \
               --socket="$TS_SOCKET" \
               --tun=userspace-networking &

    for i in $(seq 1 10); do
        if [ -S "$TS_SOCKET" ]; then
            break
        fi
        sleep 1
    done

    if [ ! -S "$TS_SOCKET" ]; then
        echo "Warning: tailscaled socket not ready after 10s (non-fatal, continuing without Tailscale)"
    else
        if tailscale --socket="$TS_SOCKET" up \
                  --authkey="$TS_AUTHKEY" \
                  --hostname="${TS_HOSTNAME:-homespun}" \
                  --accept-routes \
                  --reset 2>&1; then
            echo "Tailscale connected as ${TS_HOSTNAME:-homespun}"
        else
            echo "Warning: Tailscale failed to connect (non-fatal, continuing without Tailscale)"
        fi

        echo "Enabling Tailscale HTTPS serve..."
        tailscale --socket="$TS_SOCKET" serve --bg --https=443 http://127.0.0.1:8080 || true
        echo "Tailscale HTTPS proxy enabled on port 443"

        tailscale --socket="$TS_SOCKET" status || true
    fi
fi

echo "Starting Homespun Server..."

exec dotnet Homespun.Server.dll
