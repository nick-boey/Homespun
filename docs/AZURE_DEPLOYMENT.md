# Azure VM Deployment

Deploy Homespun to an Azure VM using Bicep Infrastructure as Code templates. This creates a fully configured VM running Homespun with Docker containers that start automatically on boot.

## Table of contents

- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [Parameters](#parameters)
- [What gets deployed](#what-gets-deployed)
- [Post-deployment](#post-deployment)
- [Komodo variables](#komodo-variables)
- [Multi-user setup](#multi-user-setup)
- [SSL and domain setup](#ssl-and-domain-setup)
- [Teardown](#teardown)
- [Troubleshooting](#troubleshooting)

## Prerequisites

| Requirement | Notes |
|---|---|
| **Azure CLI** | [Install Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) |
| **Azure subscription** | With permissions to create resource groups and VMs |
| **SSH key pair** | `ssh-keygen -t ed25519` if you don't have one |
| **GitHub PAT** | Personal access token with `repo` scope ([create one](https://github.com/settings/tokens)) |
| **Claude Code OAuth token** | Run `claude login`, then copy from `~/.claude/.credentials.json` |

## Quick start

### 1. Log in to Azure

```bash
az login
```

### 2. Set your credentials

Populate `.env` at the repo root (this is the same file used for local development):

```bash
cp .env.example .env
# edit .env and set GITHUB_TOKEN, CLAUDE_CODE_OAUTH_TOKEN, and (optionally) TAILSCALE_AUTH_KEY
```

Then export the SSH key — the deploy script reads `.env` for you:

```bash
export HOMESPUN_SSH_PUBLIC_KEY="$(cat ~/.ssh/id_ed25519.pub)"
```

### 3. Deploy

```bash
./scripts/deploy-infra.sh
```

This creates a resource group `rg-homespun` in Australia East with a `Standard_D4s_v3` VM. The VM automatically installs Docker and starts Homespun containers via cloud-init.

### 4. Connect

After deployment completes (~5-10 minutes for cloud-init):

```bash
# SSH into the VM
ssh homespun@<public-ip>

# Monitor setup progress
ssh homespun@<public-ip> 'tail -f /var/log/homespun-setup.log'
```

Access the application at `http://<public-ip>:3001` (web UI) or `http://<public-ip>:8080` (API).

## Parameters

### Deployment script options

| Flag | Default | Description |
|---|---|---|
| `--resource-group`, `-g` | `rg-homespun` | Azure resource group name |
| `--location`, `-l` | `australiaeast` | Azure region |
| `--vm-size` | `Standard_D4s_v3` | VM size |
| `--admin-username` | `homespun` | SSH admin username |
| `--base-name` | `homespun` | Prefix for all Azure resources |
| `--ssh-key` | — | Path to SSH public key file |

### Environment variables

Deploy-time inputs (exported in your shell before running `deploy-infra.sh`):

| Variable | Required | Description |
|---|---|---|
| `HOMESPUN_SSH_PUBLIC_KEY` | Yes (or `--ssh-key`) | SSH public key for VM access |
| `HOMESPUN_DOMAIN_NAME` | Optional | Domain name for Let's Encrypt SSL |

Application credentials read from `.env` at the repo root (see `.env.example`):

| Variable | Required | Description |
|---|---|---|
| `GITHUB_TOKEN` | Recommended | GitHub PAT for repository operations |
| `CLAUDE_CODE_OAUTH_TOKEN` | Recommended | Claude Code OAuth token for AI agents |
| `TAILSCALE_AUTH_KEY` | Optional | Tailscale auth key for VPN access |

`deploy-infra.sh` sources `.env` before invoking bicep, and cloud-init writes the same values into `/opt/homespun/repo/.env` on the VM.

### Bicep parameters

The `infra/main.bicepparam` file contains defaults. Override at deploy time:

| Parameter | Default | Description |
|---|---|---|
| `resourceGroupName` | `rg-homespun` | Resource group name |
| `location` | `australiaeast` | Azure region |
| `vmSize` | `Standard_D4s_v3` | VM size (see allowed values) |
| `adminUsername` | `homespun` | VM admin username |
| `adminSshPublicKey` | — | SSH public key (required) |
| `domainName` | — | Domain for SSL (optional) |
| `githubToken` | — | GitHub PAT (secure) |
| `claudeCodeOAuthToken` | — | Claude OAuth token (secure) |
| `tailscaleAuthKey` | — | Tailscale auth key (secure) |
| `osDiskSizeGb` | `256` | OS disk size (30-1024 GB) |
| `baseName` | `homespun` | Resource name prefix |

### Allowed VM sizes

- `Standard_D2s_v3` — 2 vCPU, 8 GB RAM (minimal)
- `Standard_D4s_v3` — 4 vCPU, 16 GB RAM (recommended)
- `Standard_D8s_v3` — 8 vCPU, 32 GB RAM (multi-user)
- `Standard_D2s_v5`, `Standard_D4s_v5`, `Standard_D8s_v5` — v5 equivalents

## What gets deployed

The Bicep templates create the following Azure resources in a single resource group:

| Resource | Name | Purpose |
|---|---|---|
| **Resource Group** | `rg-homespun` | Container for all resources |
| **Virtual Network** | `homespun-vnet` | Network with `10.0.0.0/16` address space |
| **Subnet** | `default` | `10.0.1.0/24` subnet |
| **NSG** | `homespun-nsg` | Firewall rules for SSH (22), HTTP (80), HTTPS (443) |
| **Public IP** | `homespun-pip` | Static public IP address |
| **NIC** | `homespun-nic` | Network interface |
| **VM** | `homespun-vm` | Ubuntu 22.04 LTS with system-assigned managed identity |

### Cloud-init setup

On first boot, the VM automatically:

1. Installs Docker and Docker Compose
2. Creates the `homespun` user
3. Clones the Homespun repository
4. Configures credentials from Bicep parameters
5. Sets up Let's Encrypt SSL (if domain provided)
6. Starts Homespun containers via `run.sh`
7. Installs Komodo via `install-komodo.sh` and starts it via `run-komodo.sh`
8. Enables a systemd service for auto-restart on reboot

Komodo and Homespun share the `homespun-net` Docker network and the single Tailscale sidecar (when a Tailscale auth key is configured). Komodo failures during cloud-init are logged as warnings but do not abort the Homespun deployment — inspect `/var/log/homespun-setup.log` on the VM to see what happened.

## Post-deployment

### Verify the setup

```bash
# SSH into the VM
ssh homespun@<public-ip>

# Check cloud-init status
cloud-init status

# Check Docker containers
docker ps

# Check application health
curl http://localhost:8080/health

# View setup logs
cat /var/log/homespun-setup.log
```

### Update credentials after deployment

SSH into the VM and edit `/opt/homespun/repo/.env` (the same `.env` the VM was deployed with):

```bash
ssh homespun@<public-ip>
cd /opt/homespun/repo
nano .env
# Edit tokens, then restart:
./scripts/run.sh --stop
./scripts/run.sh --pull
```

### Update Homespun

```bash
ssh homespun@<public-ip>
cd /opt/homespun/repo
git pull
./scripts/run.sh --stop
./scripts/run.sh --pull
```

### Access Komodo

Komodo is installed alongside Homespun by cloud-init. The admin password is generated on the VM — retrieve it with:

```bash
ssh homespun@<public-ip>
sudo grep KOMODO_INIT_ADMIN /etc/komodo/compose.env
```

Komodo's Core API listens on port `9120` (local only — not exposed in the NSG by default). To reach the Komodo UI from another host, use the Tailscale sidecar at `https://<tailscale-hostname>:3500`.

To restart, update, or reinstall Komodo:

```bash
ssh homespun@<public-ip>
cd /opt/homespun/repo
./scripts/run-komodo.sh --stop
./scripts/run-komodo.sh            # restart with the same config
./scripts/install-komodo.sh --clean  # wipe MongoDB volumes and re-provision
```

## Komodo variables

When you click **Deploy** in the Komodo UI, Komodo reads `config/komodo/resources.toml` from the GitHub repo to learn how to run the Homespun stack. That file references several values as `[[PLACEHOLDER]]` entries resolved from Komodo's **Variable store**. **Every variable below must be defined before the first deploy** — any missing variable is written out as a literal `[[VAR]]` string, which breaks `docker compose pull` with errors like `service "homespun" refers to undefined volume [[HOMESPUN_HOME]]/.homespun-container/data: invalid compose project`.

### Required variables

All seven live in **Settings → Variables** in the Komodo UI.

**Secrets** (one-off values that don't depend on the host):

| Variable | Purpose |
|---|---|
| `GITHUB_TOKEN` | GitHub PAT with `repo` scope. |
| `CLAUDE_CODE_OAUTH_TOKEN` | Claude Code OAuth token — copy from `~/.claude/.credentials.json` after `claude login`. |
| `GRAFANA_ADMIN_PASSWORD` | Grafana admin password for the bundled PLG dashboard. |

**Release knob:**

| Variable | Purpose |
|---|---|
| `HOMESPUN_TAG` | Image tag — `latest`, `alpha`, or a specific version such as `0.5.2`. Note the singular name. |

**Host-specific** (one set per server running the Komodo Periphery that hosts this stack):

| Variable | Purpose | How to derive on the host |
|---|---|---|
| `HOMESPUN_HOME` | Admin user's home directory — base for `DATA_DIR`, `SSH_DIR`, `CLAUDE_CREDENTIALS` bind mounts. | `getent passwd $ADMIN_USERNAME \| cut -d: -f6` (or `echo $HOME` as the admin user). |
| `HOST_UID` | Admin user's UID — the Homespun container runs as this user. | `id -u $ADMIN_USERNAME` |
| `HOST_GID` | Admin user's primary GID. | `id -g $ADMIN_USERNAME` |
| `DOCKER_GID` | GID of `/var/run/docker.sock` — added to the container's supplementary groups so it can spawn agent containers (Docker-out-of-Docker). Varies by distro. | `stat -c '%g' /var/run/docker.sock` |

If `DATA_DIR` points at a directory that doesn't exist on the host, Docker auto-creates it as `root:root`, and the Homespun container (running as `HOST_UID`) fails with `UnauthorizedAccessException: Access to the path '/data/DataProtection-Keys' is denied` on startup. Keeping the four host-specific variables accurate prevents that.

### Automatic sync (bundled installer)

On an Azure VM provisioned via this repo's cloud-init, `scripts/install-komodo.sh` runs automatically. It detects the four host-specific values and writes them to `/etc/komodo/homespun-vars.env`. Then `scripts/run-komodo.sh` invokes `scripts/sync-komodo-vars.sh` after Komodo Core becomes healthy, which logs in as the admin user and pushes the values into Komodo via its write API.

For this path you don't normally need to do anything manually beyond setting the three secrets and `HOMESPUN_TAG` in the Komodo UI.

#### Re-syncing after a change

If you change the admin user or the VM's Docker group GID shifts (rare, but possible after a Docker package upgrade), re-sync:

```bash
ssh homespun@<public-ip>
sudo /opt/homespun/repo/scripts/install-komodo.sh   # re-detects values into homespun-vars.env
sudo /opt/homespun/repo/scripts/sync-komodo-vars.sh # pushes them to Komodo
```

To force a specific admin user (e.g., after changing `--admin-username`):

```bash
sudo ADMIN_USERNAME=alice /opt/homespun/repo/scripts/install-komodo.sh
sudo /opt/homespun/repo/scripts/sync-komodo-vars.sh
```

Then click **Deploy** again on the `homespun` stack in the Komodo UI.

### Manual install (existing Komodo instance, no bundled scripts)

If you're hooking the stack up to a Komodo instance you installed outside this repo's scripts — or the automatic sync failed — create all seven variables by hand. On the host where Komodo will deploy Homespun, as the admin user:

```bash
echo "HOMESPUN_HOME=$HOME"
echo "HOST_UID=$(id -u)"
echo "HOST_GID=$(id -g)"
echo "DOCKER_GID=$(stat -c '%g' /var/run/docker.sock)"
```

Then in the Komodo UI → **Settings → Variables**, create each variable listed in the tables above, using these host-derived values plus your three secrets and a valid `HOMESPUN_TAG`. Finally, open the **homespun** stack and click **Deploy**.

Verify the substitution worked — no `[[...]]` should remain:

```bash
sudo cat /etc/komodo/stacks/homespun/.env
```

#### Push via curl instead of the UI

```bash
# On the Komodo Core host, grab admin credentials + values:
sudo grep KOMODO_INIT_ADMIN /etc/komodo/compose.env

JWT=$(curl -sX POST http://localhost:9120/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"type":"LoginLocalUser","params":{"username":"admin","password":"<admin-pass>"}}' \
  | jq -r .data.jwt)

curl -X POST http://localhost:9120/write/UpdateVariableValue \
  -H "Authorization: $JWT" -H 'Content-Type: application/json' \
  -d '{"name":"DOCKER_GID","value":"999"}'
```

## Multi-user setup

Each user needs their own Homespun instance. On a single VM, give each user their own clone of the repo (each with its own `.env`) and run on different ports:

```bash
# User: alice — clone to ~/homespun-alice with its own .env
cd ~/homespun-alice
./scripts/run.sh --port 8080 --container-name homespun-alice \
  --data-dir ~/.homespun-container/alice/data

# User: bob — clone to ~/homespun-bob with its own .env
cd ~/homespun-bob
./scripts/run.sh --port 8081 --container-name homespun-bob \
  --data-dir ~/.homespun-container/bob/data
```

For full isolation, deploy separate VMs using different resource group names:

```bash
./scripts/deploy-infra.sh --resource-group rg-homespun-alice --base-name homespun-alice
./scripts/deploy-infra.sh --resource-group rg-homespun-bob --base-name homespun-bob
```

See [multi-user.md](multi-user.md) for detailed per-user configuration.

## SSL and domain setup

### With a domain name

Pass `HOMESPUN_DOMAIN_NAME` at deploy time for automatic Let's Encrypt SSL:

```bash
export HOMESPUN_DOMAIN_NAME="homespun.example.com"
./scripts/deploy-infra.sh
```

After deployment, create a DNS A record pointing your domain to the VM's public IP.

### Without a domain

The application is accessible over HTTP on the VM's public IP. For secure access without a domain, use Tailscale — set `TAILSCALE_AUTH_KEY` in `.env`, then run:

```bash
./scripts/deploy-infra.sh
```

## Teardown

Remove all Azure resources:

```bash
# Interactive (prompts for confirmation)
./scripts/teardown-infra.sh

# Custom resource group
./scripts/teardown-infra.sh --resource-group rg-homespun-alice

# Skip confirmation
./scripts/teardown-infra.sh --yes
```

This deletes the entire resource group and all resources within it, including the VM, disks, network, and public IP.

## Troubleshooting

### Cloud-init didn't complete

```bash
# Check cloud-init status and logs
cloud-init status --long
cat /var/log/cloud-init-output.log
cat /var/log/homespun-setup.log
```

### Docker containers not running

```bash
# Check Docker is installed and running
systemctl status docker
docker ps -a

# Manually start Homespun (run.sh reads /opt/homespun/repo/.env automatically)
cd /opt/homespun/repo
./scripts/run.sh --pull
```

### Can't connect to the VM

- Verify the NSG allows SSH (port 22) — it does by default
- Check the VM is running: `az vm show --resource-group rg-homespun --name homespun-vm --query powerState`
- Verify your SSH key matches: `az vm show --resource-group rg-homespun --name homespun-vm --query osProfile.linuxConfiguration.ssh`

### Can't access web UI

- Verify the NSG allows HTTP (port 80) and HTTPS (port 443)
- Ports 8080 (API) and 3001 (Web UI) are not in the NSG by default — the cloud-init setup runs containers on these ports behind the standard HTTP/HTTPS ports if SSL is configured
- For direct port access, add NSG rules or use Tailscale

### Redeploying

Bicep deployments are idempotent. Running `deploy-infra.sh` again updates existing resources without recreating them. However, cloud-init only runs on first boot — to re-provision the VM software, either:

1. Delete and redeploy: `./scripts/teardown-infra.sh --yes && ./scripts/deploy-infra.sh`
2. SSH in and run the setup manually: `sudo /opt/homespun/setup.sh`
