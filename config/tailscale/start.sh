#!/bin/sh
# Tailscale Sidecar Startup Script
# Generates a serve config JSON with resolved Docker IPs, then hands off
# to containerboot for reliable netstack integration.
#
# Environment variables (from docker-compose.yml):
#   TS_AUTHKEY       - Tailscale auth key for authentication
#   TS_STATE_DIR     - Directory for Tailscale state persistence
#   TS_USERSPACE     - Enable userspace networking (no /dev/net/tun needed)
#   TS_SERVE_CONFIG  - Path where serve config JSON is written
#   TS_EXTRA_ARGS    - Extra arguments for tailscale up

set -e

echo "Starting Tailscale sidecar..."

# Wait for Homespun health check (safety net alongside depends_on: service_healthy)
echo "Waiting for Homespun to be healthy..."
for i in $(seq 1 30); do
    if wget -q --spider http://homespun:8080/health 2>/dev/null; then
        echo "Homespun is healthy"
        break
    fi
    echo "Waiting for Homespun health check... ($i/30)"
    sleep 2
done

# Resolve Docker hostnames to IPs.
# In userspace networking mode, Tailscale's netstack handles DNS independently
# and cannot resolve Docker DNS hostnames, so we must use IP addresses.
HOMESPUN_IP=$(getent hosts homespun | awk '{print $1}')
if [ -z "$HOMESPUN_IP" ]; then
    echo "Error: Could not resolve homespun hostname to IP"
    exit 1
fi
echo "Resolved homespun -> $HOMESPUN_IP"

GRAFANA_IP=$(getent hosts homespun-grafana | awk '{print $1}' 2>/dev/null || true)
if [ -n "$GRAFANA_IP" ]; then
    echo "Resolved homespun-grafana -> $GRAFANA_IP"
fi

# Generate serve config JSON for containerboot.
# containerboot substitutes ${TS_CERT_DOMAIN} with the actual Tailscale FQDN.
# printf is used so ${TS_CERT_DOMAIN} stays literal (inside single-quoted format strings)
# while resolved IPs are substituted via %s.
SERVE_DIR=$(dirname "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}")
mkdir -p "$SERVE_DIR"

{
  printf '{\n'
  printf '  "TCP": {\n'
  printf '    "443": { "HTTPS": true },\n'
  printf '    "80": { "HTTP": true }'
  if [ -n "$GRAFANA_IP" ]; then
    printf ',\n    "3000": { "HTTPS": true }'
  fi
  printf '\n  },\n'
  printf '  "Web": {\n'
  printf '    "${TS_CERT_DOMAIN}:443": {\n'
  printf '      "Handlers": { "/": { "Proxy": "http://%s:8080" } }\n' "$HOMESPUN_IP"
  printf '    },\n'
  printf '    "${TS_CERT_DOMAIN}:80": {\n'
  printf '      "Handlers": { "/": { "Proxy": "http://%s:8080" } }\n' "$HOMESPUN_IP"
  printf '    }'
  if [ -n "$GRAFANA_IP" ]; then
    printf ',\n    "${TS_CERT_DOMAIN}:3000": {\n'
    printf '      "Handlers": { "/": { "Proxy": "http://%s:3000" } }\n' "$GRAFANA_IP"
    printf '    }'
  fi
  printf '\n  }\n'
  printf '}\n'
} > "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}"

echo "Generated serve config:"
cat "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}"

# Hand off to containerboot which handles:
# - Starting tailscaled
# - Running tailscale up (with TS_AUTHKEY, TS_HOSTNAME, TS_EXTRA_ARGS)
# - Applying the serve config from TS_SERVE_CONFIG
echo "Handing off to containerboot..."
exec /usr/local/bin/containerboot
