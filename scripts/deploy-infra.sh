#!/bin/bash
set -euo pipefail

# ============================================================================
# Homespun Azure Infrastructure Deployment
# ============================================================================
#
# Deploys Homespun infrastructure to Azure using Bicep templates.
# Creates a resource group with a VM pre-configured to run Homespun.
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - SSH key pair generated
#
# Usage:
#   ./scripts/deploy-infra.sh                          # Deploy with defaults
#   ./scripts/deploy-infra.sh --resource-group my-rg   # Custom resource group
#   ./scripts/deploy-infra.sh --location westus2        # Custom region
#   ./scripts/deploy-infra.sh --vm-size Standard_D2s_v3 # Smaller VM
#
# Environment Variables:
#   HOMESPUN_SSH_PUBLIC_KEY       - SSH public key (required, or use --ssh-key)
#   HOMESPUN_DOMAIN_NAME          - Domain name for SSL (optional)
#
# Application credentials are read from .env at the repo root
# (copy .env.example to .env). The following are passed to the Azure VM:
#   GITHUB_TOKEN
#   CLAUDE_CODE_OAUTH_TOKEN
#   TAILSCALE_AUTH_KEY (optional)
#
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
INFRA_DIR="$REPO_ROOT/infra"

# Load credentials from .env at repo root so bicepparam's
# readEnvironmentVariable() calls can pick them up.
DOTENV_FILE="$REPO_ROOT/.env"
if [ -f "$DOTENV_FILE" ]; then
    set -a
    # shellcheck disable=SC1090
    source "$DOTENV_FILE"
    set +a
fi

# Default values
RESOURCE_GROUP="rg-homespun"
LOCATION="australiaeast"
VM_SIZE="Standard_D4s_v3"
ADMIN_USERNAME="homespun"
BASE_NAME="homespun"
DEPLOYMENT_NAME="homespun-$(date +%Y%m%d-%H%M%S)"
SSH_KEY_FILE=""

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
    head -25 "$0" | tail -20
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --resource-group|-g) RESOURCE_GROUP="$2"; shift ;;
        --location|-l) LOCATION="$2"; shift ;;
        --vm-size) VM_SIZE="$2"; shift ;;
        --admin-username) ADMIN_USERNAME="$2"; shift ;;
        --base-name) BASE_NAME="$2"; shift ;;
        --ssh-key) SSH_KEY_FILE="$2"; shift ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

echo
log_info "=== Homespun Azure Infrastructure Deployment ==="
echo

# Step 1: Check prerequisites
log_info "[1/4] Checking prerequisites..."

if ! command -v az &>/dev/null; then
    log_error "Azure CLI (az) is not installed."
    log_error "Install it: https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

if ! az account show &>/dev/null 2>&1; then
    log_error "Not logged in to Azure. Run 'az login' first."
    exit 1
fi

SUBSCRIPTION=$(az account show --query name -o tsv)
log_success "      Azure CLI: logged in (subscription: $SUBSCRIPTION)"

# Step 2: Resolve SSH key
log_info "[2/4] Resolving SSH public key..."

if [ -n "$SSH_KEY_FILE" ]; then
    if [ ! -f "$SSH_KEY_FILE" ]; then
        log_error "SSH key file not found: $SSH_KEY_FILE"
        exit 1
    fi
    export HOMESPUN_SSH_PUBLIC_KEY
    HOMESPUN_SSH_PUBLIC_KEY=$(cat "$SSH_KEY_FILE")
elif [ -z "${HOMESPUN_SSH_PUBLIC_KEY:-}" ]; then
    # Try default SSH key locations
    for key_file in "$HOME/.ssh/id_ed25519.pub" "$HOME/.ssh/id_rsa.pub"; do
        if [ -f "$key_file" ]; then
            export HOMESPUN_SSH_PUBLIC_KEY
            HOMESPUN_SSH_PUBLIC_KEY=$(cat "$key_file")
            log_info "      Using SSH key from: $key_file"
            break
        fi
    done
fi

if [ -z "${HOMESPUN_SSH_PUBLIC_KEY:-}" ]; then
    log_error "No SSH public key found."
    log_error "Either:"
    log_error "  - Set HOMESPUN_SSH_PUBLIC_KEY environment variable"
    log_error "  - Use --ssh-key <path-to-public-key>"
    log_error "  - Generate a key: ssh-keygen -t ed25519"
    exit 1
fi

log_success "      SSH public key resolved"

# Step 3: Show deployment plan
log_info "[3/4] Deployment plan:"
echo
log_info "======================================"
log_info "  Deployment Configuration"
log_info "======================================"
echo "  Subscription:    $SUBSCRIPTION"
echo "  Resource Group:  $RESOURCE_GROUP"
echo "  Location:        $LOCATION"
echo "  VM Size:         $VM_SIZE"
echo "  Admin User:      $ADMIN_USERNAME"
echo "  Base Name:       $BASE_NAME"
echo "  GitHub Token:    ${GITHUB_TOKEN:+configured}${GITHUB_TOKEN:-not set}"
echo "  Claude Token:    ${CLAUDE_CODE_OAUTH_TOKEN:+configured}${CLAUDE_CODE_OAUTH_TOKEN:-not set}"
echo "  Tailscale Key:   ${TAILSCALE_AUTH_KEY:+configured}${TAILSCALE_AUTH_KEY:-not set}"
echo "  Domain:          ${HOMESPUN_DOMAIN_NAME:-not set}"
log_info "======================================"
echo

# Step 4: Deploy
log_info "[4/4] Deploying infrastructure..."

az deployment sub create \
    --name "$DEPLOYMENT_NAME" \
    --location "$LOCATION" \
    --template-file "$INFRA_DIR/main.bicep" \
    --parameters "$INFRA_DIR/main.bicepparam" \
    --parameters \
        resourceGroupName="$RESOURCE_GROUP" \
        location="$LOCATION" \
        vmSize="$VM_SIZE" \
        adminUsername="$ADMIN_USERNAME" \
        baseName="$BASE_NAME" \
    --output table

echo
log_success "=== Deployment Complete ==="
echo

# Show outputs
OUTPUTS=$(az deployment sub show \
    --name "$DEPLOYMENT_NAME" \
    --query properties.outputs \
    -o json 2>/dev/null || echo "{}")

PUBLIC_IP=$(echo "$OUTPUTS" | jq -r '.publicIpAddress.value // empty' 2>/dev/null || echo "")

if [ -n "$PUBLIC_IP" ]; then
    echo "Access your Homespun instance:"
    echo
    echo "  SSH:       ssh $ADMIN_USERNAME@$PUBLIC_IP"
    echo "  Web UI:    http://$PUBLIC_IP:3001"
    echo "  API:       http://$PUBLIC_IP:8080"
    echo "  Grafana:   http://$PUBLIC_IP:3000"
    echo
    echo "Note: The VM is running cloud-init setup. This may take 5-10 minutes."
    echo "Monitor progress: ssh $ADMIN_USERNAME@$PUBLIC_IP 'tail -f /var/log/homespun-setup.log'"
fi
