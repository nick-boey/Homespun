#!/bin/bash
set -euo pipefail

# ============================================================================
# Homespun Azure Infrastructure Teardown
# ============================================================================
#
# Removes the Homespun Azure resource group and all contained resources.
#
# Usage:
#   ./scripts/teardown-infra.sh                          # Default resource group
#   ./scripts/teardown-infra.sh --resource-group my-rg   # Custom resource group
#   ./scripts/teardown-infra.sh --yes                     # Skip confirmation
#
# ============================================================================

# Default values
RESOURCE_GROUP="rg-homespun"
SKIP_CONFIRM=false

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
    head -14 "$0" | tail -10
    exit 0
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --resource-group|-g) RESOURCE_GROUP="$2"; shift ;;
        --yes|-y) SKIP_CONFIRM=true ;;
        -h|--help) show_help ;;
        *) log_error "Unknown parameter: $1"; show_help ;;
    esac
    shift
done

echo
log_info "=== Homespun Azure Infrastructure Teardown ==="
echo

# Check prerequisites
if ! command -v az &>/dev/null; then
    log_error "Azure CLI (az) is not installed."
    exit 1
fi

if ! az account show &>/dev/null 2>&1; then
    log_error "Not logged in to Azure. Run 'az login' first."
    exit 1
fi

# Check if resource group exists
if ! az group show --name "$RESOURCE_GROUP" &>/dev/null 2>&1; then
    log_warn "Resource group '$RESOURCE_GROUP' does not exist. Nothing to teardown."
    exit 0
fi

# Show what will be deleted
log_warn "The following resource group and ALL its resources will be deleted:"
echo
echo "  Resource Group: $RESOURCE_GROUP"
echo "  Location:       $(az group show --name "$RESOURCE_GROUP" --query location -o tsv)"
echo
echo "  Resources:"
az resource list --resource-group "$RESOURCE_GROUP" --query "[].{Name:name, Type:type}" -o table 2>/dev/null || true
echo

# Confirm deletion
if [ "$SKIP_CONFIRM" = false ]; then
    log_warn "This action is IRREVERSIBLE. All data on the VM will be lost."
    read -rp "Type the resource group name to confirm deletion: " CONFIRM
    if [ "$CONFIRM" != "$RESOURCE_GROUP" ]; then
        log_error "Confirmation does not match. Aborting."
        exit 1
    fi
fi

# Delete resource group
log_info "Deleting resource group '$RESOURCE_GROUP'..."
az group delete --name "$RESOURCE_GROUP" --yes --no-wait

echo
log_success "Deletion initiated. The resource group is being removed in the background."
log_info "Check status: az group show --name '$RESOURCE_GROUP' 2>/dev/null || echo 'Deleted'"
