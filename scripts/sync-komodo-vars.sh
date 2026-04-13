#!/bin/bash
set -euo pipefail

# ============================================================================
# Komodo Variables Sync
# ============================================================================
#
# Pushes host-specific values into Komodo Core's Variable store so that
# config/komodo/resources.toml can reference them via [[HOMESPUN_HOME]],
# [[HOST_UID]], [[HOST_GID]], [[DOCKER_GID]] placeholders during stack
# redeploys from the Komodo UI.
#
# Source values from /etc/komodo/homespun-vars.env (written by install-komodo.sh)
# and admin credentials from /etc/komodo/compose.env. Called automatically by
# run-komodo.sh after Core becomes healthy; safe to re-run manually.
#
# Usage:
#   sudo ./scripts/sync-komodo-vars.sh              # Sync using defaults
#   sudo ADMIN_USERNAME=foo ./scripts/sync-komodo-vars.sh   # Override admin user
#
# Exits non-zero only on hard configuration errors (missing files, bad
# credentials). Transient API failures log manual-repair instructions and
# exit 0 so a redeploy can still proceed.
# ============================================================================

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

KOMODO_DIR="/etc/komodo"
VARS_FILE="$KOMODO_DIR/homespun-vars.env"
COMPOSE_ENV="$KOMODO_DIR/compose.env"
KOMODO_CORE_URL="${KOMODO_CORE_URL:-http://localhost:9120}"

echo
log_info "=== Komodo Variables Sync ==="
echo

if [ ! -f "$VARS_FILE" ]; then
    log_error "Variables file not found: $VARS_FILE"
    log_error "Run install-komodo.sh first."
    exit 1
fi

if [ ! -f "$COMPOSE_ENV" ]; then
    log_error "Komodo compose env not found: $COMPOSE_ENV"
    log_error "Run install-komodo.sh first."
    exit 1
fi

# Load detected values (HOMESPUN_HOME, HOST_UID, HOST_GID, DOCKER_GID)
# shellcheck disable=SC1090
source "$VARS_FILE"

# Load admin credentials from compose.env. These lines are shell-compatible
# KEY=VALUE pairs, so sourcing is safe.
# shellcheck disable=SC1090
source "$COMPOSE_ENV"

ADMIN_USER="${KOMODO_INIT_ADMIN_USERNAME:-}"
ADMIN_PASS="${KOMODO_INIT_ADMIN_PASSWORD:-}"

if [ -z "$ADMIN_USER" ] || [ -z "$ADMIN_PASS" ]; then
    log_error "Admin credentials not found in $COMPOSE_ENV"
    log_error "Expected KOMODO_INIT_ADMIN_USERNAME and KOMODO_INIT_ADMIN_PASSWORD."
    exit 1
fi

# Wait for Komodo Core to be reachable (up to ~90s)
log_info "[1/3] Waiting for Komodo Core at $KOMODO_CORE_URL..."
for _ in $(seq 1 45); do
    if curl -sf -o /dev/null "$KOMODO_CORE_URL/"; then
        log_success "      Core is reachable."
        break
    fi
    sleep 2
done

if ! curl -sf -o /dev/null "$KOMODO_CORE_URL/"; then
    log_warn "      Core did not respond within timeout."
    log_warn "      Skipping variable sync. Re-run this script after Core is up:"
    log_warn "        sudo $0"
    exit 0
fi

# Log in as admin to obtain a JWT
log_info "[2/3] Authenticating as admin '$ADMIN_USER'..."
LOGIN_BODY=$(jq -nc --arg u "$ADMIN_USER" --arg p "$ADMIN_PASS" \
    '{type:"LoginLocalUser",params:{username:$u,password:$p}}')

LOGIN_RESP=$(curl -sS -X POST "$KOMODO_CORE_URL/auth/login" \
    -H "Content-Type: application/json" \
    -d "$LOGIN_BODY") || {
    log_warn "      Login request failed. Skipping variable sync."
    log_warn "      Set variables manually in Komodo UI (see docs/deployment/komodo-variables.md)."
    exit 0
}

JWT=$(echo "$LOGIN_RESP" | jq -r '.data.jwt // empty')
if [ -z "$JWT" ]; then
    log_warn "      Login did not return a JWT. Response: $LOGIN_RESP"
    log_warn "      Set variables manually in Komodo UI (see docs/deployment/komodo-variables.md)."
    exit 0
fi
log_success "      Authenticated."

# Push each variable via CreateVariable; on duplicate, fall back to
# UpdateVariableValue. Komodo's CreateVariable returns 4xx when a variable
# already exists, so try create first then update.
push_variable() {
    local name="$1"
    local value="$2"
    local description="$3"

    local create_body
    create_body=$(jq -nc \
        --arg n "$name" \
        --arg v "$value" \
        --arg d "$description" \
        '{name:$n, value:$v, description:$d, is_secret:false}')

    local http_code
    http_code=$(curl -sS -o /tmp/komodo-sync-resp.txt -w '%{http_code}' \
        -X POST "$KOMODO_CORE_URL/write/CreateVariable" \
        -H "Authorization: $JWT" \
        -H "Content-Type: application/json" \
        -d "$create_body") || http_code="000"

    if [ "$http_code" = "200" ]; then
        log_success "      Created $name=$value"
        return 0
    fi

    # Any non-200 response on create -> try update
    local update_body
    update_body=$(jq -nc --arg n "$name" --arg v "$value" \
        '{name:$n, value:$v}')

    http_code=$(curl -sS -o /tmp/komodo-sync-resp.txt -w '%{http_code}' \
        -X POST "$KOMODO_CORE_URL/write/UpdateVariableValue" \
        -H "Authorization: $JWT" \
        -H "Content-Type: application/json" \
        -d "$update_body") || http_code="000"

    if [ "$http_code" = "200" ]; then
        log_success "      Updated $name=$value"
        return 0
    fi

    log_warn "      Failed to sync $name (HTTP $http_code)"
    log_warn "      Response: $(cat /tmp/komodo-sync-resp.txt 2>/dev/null | head -c 200)"
    return 1
}

log_info "[3/3] Pushing host-specific variables to Komodo..."
FAILED=0
push_variable "HOMESPUN_HOME" "$HOMESPUN_HOME" \
    "Admin user's home directory (auto-detected from $VARS_FILE)." || FAILED=$((FAILED+1))
push_variable "HOST_UID" "$HOST_UID" \
    "Admin user's UID for Homespun container (auto-detected from $VARS_FILE)." || FAILED=$((FAILED+1))
push_variable "HOST_GID" "$HOST_GID" \
    "Admin user's primary GID for Homespun container (auto-detected from $VARS_FILE)." || FAILED=$((FAILED+1))
push_variable "DOCKER_GID" "$DOCKER_GID" \
    "GID of the host /var/run/docker.sock group (auto-detected from $VARS_FILE)." || FAILED=$((FAILED+1))

rm -f /tmp/komodo-sync-resp.txt

echo
if [ "$FAILED" -eq 0 ]; then
    log_success "All Komodo Variables synced successfully."
else
    log_warn "$FAILED variable(s) failed to sync."
    log_warn "To repair manually, log in to the Komodo UI and open:"
    log_warn "  Settings -> Variables"
    log_warn "Set these four entries to the values shown above, then redeploy the Homespun stack."
fi
echo
