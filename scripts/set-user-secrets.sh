#!/usr/bin/env bash
# ============================================================================
# One-shot migration: .env → dotnet user-secrets (Homespun.AppHost)
# ============================================================================
#
# Reads GITHUB_TOKEN and CLAUDE_CODE_OAUTH_TOKEN from .env at repo root and
# stores them under `Parameters:github-token` / `Parameters:claude-oauth-token`
# in the user-secrets store scoped to src/Homespun.AppHost so that Aspire
# resolves them at dev-run time (any launch profile).
#
# Idempotent: missing .env or blank key → warn and skip; does not clear
# existing user-secrets.

set -eu

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$PROJECT_ROOT/.env"
APPHOST_PROJECT="$PROJECT_ROOT/src/Homespun.AppHost"

if [ ! -f "$ENV_FILE" ]; then
    echo "WARN: $ENV_FILE not found — nothing to migrate." >&2
    exit 0
fi

# Extract a value for KEY from .env, stripping optional surrounding quotes.
env_value() {
    local key="$1"
    local line
    line=$(grep -E "^[[:space:]]*${key}=" "$ENV_FILE" | tail -n 1 || true)
    if [ -z "$line" ]; then
        return 0
    fi
    local value="${line#*=}"
    # Strip optional surrounding single or double quotes.
    if [[ "$value" == \"*\" ]]; then value="${value%\"}"; value="${value#\"}"; fi
    if [[ "$value" == \'*\' ]]; then value="${value%\'}"; value="${value#\'}"; fi
    printf '%s' "$value"
}

set_secret() {
    local key="$1"
    local value="$2"
    local secret_name="$3"
    if [ -z "$value" ]; then
        echo "WARN: $key is blank in .env — leaving user-secret '$secret_name' untouched." >&2
        return 0
    fi
    dotnet user-secrets set "$secret_name" "$value" --project "$APPHOST_PROJECT" >/dev/null
    echo "OK:   $secret_name set from $key."
}

GITHUB_TOKEN_VALUE=$(env_value "GITHUB_TOKEN")
CLAUDE_TOKEN_VALUE=$(env_value "CLAUDE_CODE_OAUTH_TOKEN")

set_secret "GITHUB_TOKEN" "$GITHUB_TOKEN_VALUE" "Parameters:github-token"
set_secret "CLAUDE_CODE_OAUTH_TOKEN" "$CLAUDE_TOKEN_VALUE" "Parameters:claude-oauth-token"
