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

# Resolve Docker hostnames to IPs.
# In userspace networking mode, Tailscale's netstack handles DNS independently
# and cannot resolve Docker DNS hostnames, so we must use IP addresses.
# All services are optional - the script serves whichever are available.
echo "Resolving service hostnames..."

HOMESPUN_IP=$(getent hosts homespun | awk '{print $1}' 2>/dev/null || true)
if [ -n "$HOMESPUN_IP" ]; then
    echo "Resolved homespun -> $HOMESPUN_IP"
fi

GRAFANA_IP=$(getent hosts homespun-grafana | awk '{print $1}' 2>/dev/null || true)
if [ -n "$GRAFANA_IP" ]; then
    echo "Resolved homespun-grafana -> $GRAFANA_IP"
fi

KOMODO_IP=$(getent hosts homespun-komodo-core | awk '{print $1}' 2>/dev/null || true)
if [ -n "$KOMODO_IP" ]; then
    echo "Resolved homespun-komodo-core -> $KOMODO_IP"
fi

# Require at least one service to be resolvable
if [ -z "$HOMESPUN_IP" ] && [ -z "$GRAFANA_IP" ] && [ -z "$KOMODO_IP" ]; then
    echo "Error: Could not resolve any service hostnames"
    exit 1
fi

# Generate serve config JSON for containerboot.
# containerboot substitutes ${TS_CERT_DOMAIN} with the actual Tailscale FQDN.
# printf is used so ${TS_CERT_DOMAIN} stays literal (inside single-quoted format strings)
# while resolved IPs are substituted via %s.
SERVE_DIR=$(dirname "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}")
mkdir -p "$SERVE_DIR"

{
  printf '{\n'

  # Build TCP section - collect entries, then join with commas
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

  # Build Web section - collect entries, then join with commas
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
} > "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}"

echo "Generated serve config:"
cat "${TS_SERVE_CONFIG:-/tmp/serve/serve-config.json}"

# Hand off to containerboot which handles:
# - Starting tailscaled
# - Running tailscale up (with TS_AUTHKEY, TS_HOSTNAME, TS_EXTRA_ARGS)
# - Applying the serve config from TS_SERVE_CONFIG
echo "Handing off to containerboot..."
exec /usr/local/bin/containerboot
