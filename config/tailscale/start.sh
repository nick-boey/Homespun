#!/bin/sh
# Tailscale Sidecar Startup Script — Dynamic Service Discovery
#
# Starts containerboot in the background, then polls for Docker service
# hostnames every 15 seconds. When the set of available services changes,
# the serve-config JSON is regenerated and containerboot picks up the
# change via inotify.
#
# Environment variables (from compose.yml):
#   TS_AUTHKEY       - Tailscale auth key for authentication
#   TS_STATE_DIR     - Directory for Tailscale state persistence
#   TS_USERSPACE     - Enable userspace networking (no /dev/net/tun needed)
#   TS_SERVE_CONFIG  - Path where serve config JSON is written
#   TS_EXTRA_ARGS    - Extra arguments for tailscale up

set -e

SERVE_CONFIG="${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}"
SERVE_DIR="$(dirname "$SERVE_CONFIG")"
POLL_INTERVAL=15

# Track the last-known set of resolved services so we only rewrite when
# something actually changes.
PREV_STATE=""

# ── helpers ──────────────────────────────────────────────────────────

resolve() {
    # Resolve a hostname, return the IP or empty string
    getent hosts "$1" 2>/dev/null | awk '{print $1}' || true
}

generate_config() {
    # Resolve all known service hostnames
    HOMESPUN_IP=$(resolve homespun)
    GRAFANA_IP=$(resolve homespun-grafana)
    KOMODO_IP=$(resolve homespun-komodo-core)

    # Build a comparable state string
    STATE="homespun=$HOMESPUN_IP grafana=$GRAFANA_IP komodo=$KOMODO_IP"

    # Short-circuit if nothing changed
    if [ "$STATE" = "$PREV_STATE" ]; then
        return 1  # no change
    fi
    PREV_STATE="$STATE"

    [ -n "$HOMESPUN_IP" ] && echo "  resolved homespun -> $HOMESPUN_IP"
    [ -n "$GRAFANA_IP" ]  && echo "  resolved homespun-grafana -> $GRAFANA_IP"
    [ -n "$KOMODO_IP" ]   && echo "  resolved homespun-komodo-core -> $KOMODO_IP"

    # Write the serve-config JSON
    mkdir -p "$SERVE_DIR"
    {
        printf '{\n'

        # TCP section
        printf '  "TCP": {\n'
        TCP_ENTRIES=""
        if [ -n "$HOMESPUN_IP" ]; then
            TCP_ENTRIES='    "443": { "HTTPS": true },\n    "80": { "HTTP": true }'
        fi
        if [ -n "$GRAFANA_IP" ]; then
            [ -n "$TCP_ENTRIES" ] && TCP_ENTRIES="$TCP_ENTRIES,"
            TCP_ENTRIES="$TCP_ENTRIES"'\n    "3000": { "HTTPS": true }'
        fi
        if [ -n "$KOMODO_IP" ]; then
            [ -n "$TCP_ENTRIES" ] && TCP_ENTRIES="$TCP_ENTRIES,"
            TCP_ENTRIES="$TCP_ENTRIES"'\n    "3500": { "HTTPS": true }'
        fi
        printf "$TCP_ENTRIES"
        printf '\n  },\n'

        # Web section
        printf '  "Web": {\n'
        WEB_FIRST=true
        if [ -n "$HOMESPUN_IP" ]; then
            printf '    "${TS_CERT_DOMAIN}:443": {\n'
            printf '      "Handlers": { "/": { "Proxy": "http://%s:8080" } }\n' "$HOMESPUN_IP"
            printf '    },\n'
            printf '    "${TS_CERT_DOMAIN}:80": {\n'
            printf '      "Handlers": { "/": { "Proxy": "http://%s:8080" } }\n' "$HOMESPUN_IP"
            printf '    }'
            WEB_FIRST=false
        fi
        if [ -n "$GRAFANA_IP" ]; then
            [ "$WEB_FIRST" = false ] && printf ','
            printf '\n    "${TS_CERT_DOMAIN}:3000": {\n'
            printf '      "Handlers": { "/": { "Proxy": "http://%s:3000" } }\n' "$GRAFANA_IP"
            printf '    }'
            WEB_FIRST=false
        fi
        if [ -n "$KOMODO_IP" ]; then
            [ "$WEB_FIRST" = false ] && printf ','
            printf '\n    "${TS_CERT_DOMAIN}:3500": {\n'
            printf '      "Handlers": { "/": { "Proxy": "http://%s:9120" } }\n' "$KOMODO_IP"
            printf '    }'
        fi
        printf '\n  }\n'
        printf '}\n'
    } > "$SERVE_CONFIG"

    return 0  # changed
}

# ── main ─────────────────────────────────────────────────────────────

echo "Starting Tailscale sidecar (dynamic mode)..."

# Generate initial config (may be empty TCP/Web if no services yet)
if generate_config; then
    echo "Initial serve config:"
    cat "$SERVE_CONFIG"
else
    # Even on first run we need a valid file for containerboot
    mkdir -p "$SERVE_DIR"
    printf '{ "TCP": {}, "Web": {} }\n' > "$SERVE_CONFIG"
    echo "No services found yet — wrote empty serve config."
fi

# Start containerboot in the background
echo "Starting containerboot..."
/usr/local/bin/containerboot &
BOOT_PID=$!

# Forward termination signals to containerboot
trap 'kill $BOOT_PID 2>/dev/null; wait $BOOT_PID 2>/dev/null; exit 0' TERM INT

# Poll loop — re-resolve services and regenerate config on change
echo "Entering service discovery loop (every ${POLL_INTERVAL}s)..."
while kill -0 $BOOT_PID 2>/dev/null; do
    sleep "$POLL_INTERVAL" &
    SLEEP_PID=$!
    wait $SLEEP_PID 2>/dev/null || true

    if generate_config; then
        echo "Service change detected — updated serve config:"
        cat "$SERVE_CONFIG"
    fi
done

# containerboot exited on its own
wait $BOOT_PID
