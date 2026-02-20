#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Homespun Docker container. By default, uses pre-built GHCR images.

.DESCRIPTION
    This script:
    - Validates Docker is running
    - Reads GitHub token from environment variables or .NET user secrets
    - Creates the ~/.homespun-container/data directory
    - Uses pre-built GHCR images by default
    - Use -Local for development to build images locally

.EXAMPLE
    .\run.ps1
    Production: Uses pre-built GHCR images.

.EXAMPLE
    .\run.ps1 -Local
    Development: Builds local images with Docker agent execution.

.EXAMPLE
    .\run.ps1 -LocalAgents
    Development: Uses GHCR images with in-process agent execution.

.EXAMPLE
    .\run.ps1 -DebugBuild
    Builds local images in Debug configuration.

.EXAMPLE
    .\run.ps1 -MockMode
    Runs in mock mode with seeded demo data.

.EXAMPLE
    .\run.ps1 -MockMode -Port 5095
    Runs in mock mode on a custom port.

.EXAMPLE
    .\run.ps1 -TailscaleAuthKey "tskey-auth-..."
    Runs with Tailscale enabled for HTTPS access.

.EXAMPLE
    .\run.ps1 -ExternalHostname "homespun.tail1234.ts.net"
    Runs with external hostname for agent URLs.

.EXAMPLE
    .\run.ps1 -DataDir "C:\custom\data" -ContainerName "homespun-custom"
    Runs with custom data directory and container name.

.EXAMPLE
    .\run.ps1 -Stop
    Stops all containers.

.EXAMPLE
    .\run.ps1 -Logs
    Shows container logs.

.NOTES
    Container name: homespun
    Port: 8080 (or via Tailscale HTTPS)
    Data directory: ~/.homespun-container/data

    Environment Variables (checked in order, with .env file fallback):
    - HSP_GITHUB_TOKEN / GITHUB_TOKEN - GitHub personal access token
    - HSP_TAILSCALE_AUTH_KEY / TAILSCALE_AUTH_KEY - Tailscale auth key
    - HSP_EXTERNAL_HOSTNAME - External hostname for agent URLs

    Volume Mounts:
    - Claude Code config (~/.claude) is automatically mounted for OAuth authentication
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')]
    [switch]$Local,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$LocalAgents,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$DebugBuild,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$MockMode,

    [Parameter(ParameterSetName = 'Run')]
    [int]$Port = 8080,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Interactive,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Detach,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Pull,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$NoTailscale,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$NoPlg,

    [Parameter(ParameterSetName = 'Run')]
    [int]$GrafanaPort = 3000,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleAuthKey,

    [Parameter(ParameterSetName = 'Run')]
    [string]$TailscaleHostname = "homespun",

    [Parameter(ParameterSetName = 'Run')]
    [string]$ExternalHostname,

    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'Stop')]
    [Parameter(ParameterSetName = 'Logs')]
    [string]$DataDir,

    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'Stop')]
    [Parameter(ParameterSetName = 'Logs')]
    [string]$ContainerName = "homespun",

    [Parameter(ParameterSetName = 'Stop')]
    [switch]$Stop,

    [Parameter(ParameterSetName = 'Logs')]
    [switch]$Logs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory and repository root
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

# Constants
$UserSecretsId = "2cfc6c57-72da-4b56-944b-08f2c1df76f6"
$EnvFilePath = Join-Path $RepoRoot ".env"

# ============================================================================
# Functions
# ============================================================================

function Get-EnvFileValue {
    param(
        [string]$Key,
        [string]$EnvFilePath
    )

    if (-not (Test-Path $EnvFilePath)) {
        return $null
    }

    try {
        $content = Get-Content $EnvFilePath -Raw
        # Match KEY=value, handling optional quotes
        if ($content -match "(?m)^$Key=([`"']?)(.+?)\1\s*$") {
            return $Matches[2]
        }
        # Also try without quotes for simple values
        if ($content -match "(?m)^$Key=(.+?)\s*$") {
            $value = $Matches[1] -replace '^["'']|["'']$', ''
            return $value
        }
        return $null
    }
    catch {
        return $null
    }
}

function Test-DockerRunning {
    try {
        $null = docker version 2>$null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerComposeAvailable {
    try {
        $null = docker compose version 2>$null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerImageExists {
    param([string]$ImageName)
    $images = docker images --format "{{.Repository}}:{{.Tag}}" 2>$null
    return $images -contains $ImageName
}

function Get-GitHubToken {
    param(
        [string]$UserSecretsId,
        [string]$EnvFilePath
    )

    # Check environment variables first (HSP_GITHUB_TOKEN takes precedence)
    $token = $env:HSP_GITHUB_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    $token = $env:GITHUB_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    # Fall back to .NET user secrets
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"

    if (Test-Path $secretsPath) {
        try {
            $secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
            $token = $secrets.'GitHub:Token'
            if (-not [string]::IsNullOrWhiteSpace($token)) {
                return $token
            }
        }
        catch {
            # Continue to next fallback
        }
    }

    # Fall back to .env file
    $token = Get-EnvFileValue -Key "GITHUB_TOKEN" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    return $null
}

function Get-TailscaleAuthKey {
    param(
        [string]$ParamValue,
        [string]$EnvFilePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        return $ParamValue
    }

    $key = $env:HSP_TAILSCALE_AUTH_KEY
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    $key = $env:TAILSCALE_AUTH_KEY
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    # Fall back to .env file
    $key = Get-EnvFileValue -Key "TAILSCALE_AUTH_KEY" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    return $null
}

function Get-ExternalHostname {
    param(
        [string]$ParamValue,
        [string]$EnvFilePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        return $ParamValue
    }

    $hostname = $env:HSP_EXTERNAL_HOSTNAME
    if (-not [string]::IsNullOrWhiteSpace($hostname)) {
        return $hostname
    }

    # Fall back to .env file
    $hostname = Get-EnvFileValue -Key "HSP_EXTERNAL_HOSTNAME" -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($hostname)) {
        return $hostname
    }

    return $null
}

# ============================================================================
# Main Script
# ============================================================================

Write-Host ""
Write-Host "=== Homespun Docker Compose Runner ===" -ForegroundColor Cyan
Write-Host ""

# Change to repository root for docker-compose
Push-Location $RepoRoot
try {
    # Compose file paths
    $ComposeFile = Join-Path $RepoRoot "docker-compose.yml"
    $EnvFile = Join-Path $RepoRoot ".env.compose"

    # Handle Stop action
    if ($Stop) {
        Write-Host "Stopping containers..." -ForegroundColor Cyan
        if (Test-Path $EnvFile) {
            docker compose -f $ComposeFile --env-file $EnvFile down 2>$null
        }
        docker stop $ContainerName 2>$null
        docker rm $ContainerName 2>$null
        docker stop homespun-loki homespun-promtail homespun-grafana 2>$null
        docker rm homespun-loki homespun-promtail homespun-grafana 2>$null
        Write-Host "Containers stopped." -ForegroundColor Green
        exit 0
    }

    # Handle Logs action
    if ($Logs) {
        Write-Host "Following container logs (Ctrl+C to exit)..." -ForegroundColor Cyan
        if (Test-Path $EnvFile) {
            docker compose -f $ComposeFile --env-file $EnvFile logs -f
        }
        else {
            docker logs -f $ContainerName
        }
        exit 0
    }

    # Step 1: Validate Docker is running
    Write-Host "[1/5] Checking Docker..." -ForegroundColor Cyan
    if (-not (Test-DockerRunning)) {
        Write-Error "Docker is not running. Please start Docker and try again."
    }
    if (-not (Test-DockerComposeAvailable)) {
        Write-Error "Docker Compose is not available. Please install Docker Compose."
    }
    Write-Host "      Docker and Docker Compose are available." -ForegroundColor Green

    # Step 2: Check/build image
    Write-Host "[2/5] Checking container images..." -ForegroundColor Cyan
    if ($Local) {
        # Development: Build base + both app images locally
        $ImageName = "homespun:local"
        $WorkerImage = "homespun-worker:local"
        $BaseImage = "homespun-base:local"
        $BuildConfig = if ($DebugBuild) { "Debug" } else { "Release" }

        Write-Host "      Building base tooling image..." -ForegroundColor Cyan
        $env:DOCKER_BUILDKIT = "1"
        docker build -t $BaseImage -f "$RepoRoot/Dockerfile.base" $RepoRoot
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build base Docker image."
        }
        Write-Host "      Base image built: $BaseImage" -ForegroundColor Green

        Write-Host "      Building main Homespun image ($BuildConfig)..." -ForegroundColor Cyan
        docker build -t $ImageName --build-arg BUILD_CONFIGURATION=$BuildConfig $RepoRoot
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build main Docker image."
        }
        Write-Host "      Main image built: $ImageName ($BuildConfig)" -ForegroundColor Green

        Write-Host "      Building Worker image..." -ForegroundColor Cyan
        docker build -t $WorkerImage -f "$RepoRoot/src/Homespun.Worker/Dockerfile" "$RepoRoot/src/Homespun.Worker"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build Worker Docker image."
        }
        Write-Host "      Worker image built: $WorkerImage" -ForegroundColor Green
    }
    else {
        # Production: Use GHCR images
        $ImageName = "ghcr.io/nick-boey/homespun:latest"
        $WorkerImage = "ghcr.io/nick-boey/homespun-worker:latest"
        if ($Pull) {
            Write-Host "      Pulling latest images..." -ForegroundColor Cyan
            docker pull $ImageName
            docker pull $WorkerImage
        }
        Write-Host "      Using GHCR image: $ImageName" -ForegroundColor Green
        Write-Host "      Using GHCR worker: $WorkerImage" -ForegroundColor Green
    }

    # Step 3: Read GitHub token
    Write-Host "[3/5] Reading GitHub token..." -ForegroundColor Cyan
    $githubToken = Get-GitHubToken -UserSecretsId $UserSecretsId -EnvFilePath $EnvFilePath

    if ([string]::IsNullOrWhiteSpace($githubToken)) {
        Write-Warning "      GitHub token not found."
        Write-Warning "      Set HSP_GITHUB_TOKEN or GITHUB_TOKEN environment variable."
    }
    else {
        $maskedToken = $githubToken.Substring(0, [Math]::Min(10, $githubToken.Length)) + "..."
        Write-Host "      GitHub token found: $maskedToken" -ForegroundColor Green
    }

    # Read Tailscale auth key (unless -NoTailscale)
    $tailscaleKey = $null
    if ($NoTailscale) {
        Write-Host "      Tailscale disabled (-NoTailscale flag)" -ForegroundColor Cyan
    }
    else {
        $tailscaleKey = Get-TailscaleAuthKey -ParamValue $TailscaleAuthKey -EnvFilePath $EnvFilePath
        if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
            $maskedTsKey = $tailscaleKey.Substring(0, [Math]::Min(15, $tailscaleKey.Length)) + "..."
            Write-Host "      Tailscale auth key found: $maskedTsKey" -ForegroundColor Green
        }
    }

    # Read external hostname
    $externalHostnameValue = Get-ExternalHostname -ParamValue $ExternalHostname -EnvFilePath $EnvFilePath
    if (-not [string]::IsNullOrWhiteSpace($externalHostnameValue)) {
        Write-Host "      External hostname: $externalHostnameValue" -ForegroundColor Green
    }

    # Step 4: Set up directories
    Write-Host "[4/5] Setting up directories..." -ForegroundColor Cyan
    $homeDir = [Environment]::GetFolderPath('UserProfile')
    # Use DataDir parameter if provided, otherwise default
    if ([string]::IsNullOrWhiteSpace($DataDir)) {
        $dataDir = Join-Path $homeDir ".homespun-container" "data"
    }
    else {
        $dataDir = $DataDir
    }
    $sshDir = Join-Path $homeDir ".ssh"
    $claudeCredentialsFile = Join-Path $homeDir ".claude\.credentials.json"
    $tailscaleStateDir = Join-Path $dataDir "tailscale"

    if (-not (Test-Path $dataDir)) {
        New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
        Write-Host "      Created data directory: $dataDir" -ForegroundColor Green
    }
    else {
        Write-Host "      Data directory exists: $dataDir" -ForegroundColor Green
    }

    # Create Tailscale state directory
    if (-not (Test-Path $tailscaleStateDir)) {
        New-Item -ItemType Directory -Path $tailscaleStateDir -Force | Out-Null
        Write-Host "      Created Tailscale state directory: $tailscaleStateDir" -ForegroundColor Green
    }

    # Create DataProtection-Keys directory if it doesn't exist
    $dataProtectionDir = Join-Path $dataDir "DataProtection-Keys"
    if (-not (Test-Path $dataProtectionDir)) {
        New-Item -ItemType Directory -Path $dataProtectionDir -Force | Out-Null
        Write-Host "      Created DataProtection-Keys directory" -ForegroundColor Green
    }

    # Fix permissions on data directory (needed when files were created by different user)
    # Run a quick docker command to chown the data directory to the homespun user (uid 1655)
    $dataDirUnixTemp = $dataDir -replace '\\', '/'
    docker run --rm -v "${dataDirUnixTemp}:/fixdata" alpine chown -R 1655:1655 /fixdata 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      Fixed data directory permissions" -ForegroundColor Green
    }

    if (-not (Test-Path $sshDir)) {
        Write-Warning "      SSH directory not found: $sshDir"
        $sshDir = ""
    }

    # Check Claude Code credentials file (for OAuth authentication)
    if (-not (Test-Path $claudeCredentialsFile)) {
        Write-Warning "      Claude credentials not found: $claudeCredentialsFile"
        Write-Warning "      Run 'claude login' on host to authenticate Claude Code."
        $claudeCredentialsFile = ""
    }
    else {
        Write-Host "      Claude credentials found: $claudeCredentialsFile" -ForegroundColor Green
    }

    # Mount Docker socket for DooD (Docker outside of Docker)
    # This enables containers to spawn sibling containers using the host's Docker daemon
    # On Windows, Docker uses a named pipe; on Linux/WSL, it uses a socket
    $dockerSocket = ""
    if ($IsWindows) {
        # Windows Docker uses named pipe
        $dockerSocket = "//./pipe/docker_engine:/var/run/docker.sock"
        Write-Host "      Docker socket: DooD enabled (Windows named pipe)" -ForegroundColor Green
    }
    else {
        # Linux/WSL uses Unix socket - always mount it for DooD support
        $dockerSocket = "/var/run/docker.sock:/var/run/docker.sock"
        if (Test-Path "/var/run/docker.sock") {
            Write-Host "      Docker socket: DooD enabled (/var/run/docker.sock)" -ForegroundColor Green
        }
        else {
            Write-Host "      Docker socket will be mounted: /var/run/docker.sock (DooD)" -ForegroundColor Cyan
            Write-Host "      Note: Socket must exist on host for container Docker access" -ForegroundColor Cyan
        }
    }

    # Step 5: Start containers
    Write-Host "[5/5] Starting containers..." -ForegroundColor Cyan
    Write-Host ""

    # Convert paths for Docker
    $dataDirUnix = $dataDir -replace '\\', '/'
    $sshDirUnix = if ($sshDir) { $sshDir -replace '\\', '/' } else { "/dev/null" }
    $claudeCredentialsFileUnix = if ($claudeCredentialsFile) { $claudeCredentialsFile -replace '\\', '/' } else { "/dev/null" }
    $tailscaleStateDirUnix = $tailscaleStateDir -replace '\\', '/'

    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Container Configuration" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host "  Container:   $ContainerName"
    Write-Host "  Image:       $ImageName"
    Write-Host "  Port:        $Port"
    Write-Host "  URL:         http://localhost:$Port"
    Write-Host "  Data mount:  $dataDir"
    if ($sshDir) {
        Write-Host "  SSH mount:   $sshDir (read-only)"
    }
    if ($claudeCredentialsFile) {
        Write-Host "  Claude auth: $claudeCredentialsFile (read-only)"
    }
    if (-not [string]::IsNullOrWhiteSpace($dockerSocket)) {
        Write-Host "  Docker:      DooD enabled (host socket mounted)"
    }
    if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
        Write-Host "  Tailscale:   Enabled ($TailscaleHostname)"
    }
    else {
        Write-Host "  Tailscale:   Disabled (no auth key)"
    }
    if (-not [string]::IsNullOrWhiteSpace($externalHostnameValue)) {
        Write-Host "  Agent URLs:  https://$($externalHostnameValue):<port>"
    }
    if ($MockMode) {
        Write-Host "  Mock mode:   Enabled (seeded demo data)"
    }
    if ($Local) {
        Write-Host "  Build:       Local (development mode)"
    }
    else {
        Write-Host "  Build:       GHCR (production images)"
    }
    if ($LocalAgents) {
        Write-Host "  Agents:      In-process (Local mode)"
    }
    else {
        Write-Host "  Agents:      Docker containers ($WorkerImage)"
    }
    if ($NoPlg) {
        Write-Host "  Logging:     Console only (PLG disabled)"
    }
    else {
        Write-Host "  Logging:     PLG stack (Grafana: http://localhost:$GrafanaPort)"
    }
    Write-Host "======================================" -ForegroundColor Cyan
    Write-Host ""

    # Generate .env.compose file for Docker Compose
    Write-Host "Generating $EnvFile..." -ForegroundColor Cyan

    # Determine environment values
    $aspnetEnv = if ($MockMode) { "MockLive" } else { "Production" }
    $agentMode = if ($LocalAgents) { "Local" } else { "Docker" }
    $claudeCredPath = if ($claudeCredentialsFile) { $claudeCredentialsFileUnix } else { "/dev/null" }
    $sshDirPath = if ($sshDir) { $sshDirUnix } else { "/dev/null" }

    # Get Docker socket GID (use 999 as default on Windows)
    $dockerGid = if ($IsWindows) { "0" } else { "999" }

    # Write environment file
    @"
# Generated by run.ps1 on $(Get-Date)
# Do not edit manually - regenerated on each run

# Container settings
HOMESPUN_IMAGE=$ImageName
WORKER_IMAGE=$WorkerImage
CONTAINER_NAME=$ContainerName
HOST_PORT=$Port
HOST_UID=1000
HOST_GID=1000
DOCKER_GID=$dockerGid

# Directories
DATA_DIR=$dataDirUnix
SSH_DIR=$sshDirPath
CLAUDE_CREDENTIALS=$claudeCredPath

# Environment
ASPNETCORE_ENVIRONMENT=$aspnetEnv
AGENT_MODE=$agentMode

# Credentials
GITHUB_TOKEN=$githubToken
CLAUDE_CODE_OAUTH_TOKEN=
TAILSCALE_AUTH_KEY=$tailscaleKey
TS_HOSTNAME=$TailscaleHostname
HSP_EXTERNAL_HOSTNAME=$externalHostnameValue

# Grafana
GRAFANA_PORT=$GrafanaPort
GRAFANA_ADMIN_PASSWORD=admin
"@ | Set-Content -Path $EnvFile -Encoding UTF8

    Write-Host "      Created $EnvFile" -ForegroundColor Green

    # Stop existing containers first
    Write-Host "Stopping any existing containers..." -ForegroundColor Cyan
    docker compose -f $ComposeFile --env-file $EnvFile down 2>$null
    docker stop $ContainerName 2>$null
    docker rm $ContainerName 2>$null

    # Build compose command
    $composeArgs = @("-f", $ComposeFile, "--env-file", $EnvFile)
    if (-not $NoPlg) {
        $composeArgs += "--profile", "plg"
    }

    # Determine run mode
    $runDetached = $Detach -or (-not $Interactive)

    if ($runDetached) {
        Write-Host "Starting containers in detached mode..." -ForegroundColor Cyan
        & docker compose @composeArgs up -d

        Write-Host ""
        Write-Host "Containers started successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Access URLs:"
        Write-Host "  Homespun:    http://localhost:$Port"
        if (-not $NoPlg) {
            Write-Host "  Grafana:     http://localhost:$GrafanaPort (admin/admin)"
            Write-Host "  Loki:        http://localhost:3100"
        }
        if (-not [string]::IsNullOrWhiteSpace($tailscaleKey)) {
            Write-Host "  Tailnet:     https://$TailscaleHostname.<your-tailnet>.ts.net"
        }
        Write-Host ""
        Write-Host "Useful commands:"
        Write-Host "  View logs:     .\run.ps1 -Logs"
        Write-Host "  Stop:          .\run.ps1 -Stop"
        Write-Host "  Health check:  curl http://localhost:$Port/health"
        Write-Host ""
    }
    else {
        Write-Warning "Starting containers in interactive mode..."
        Write-Warning "Press Ctrl+C to stop."
        Write-Host ""
        & docker compose @composeArgs up
        Write-Host ""
        Write-Warning "Containers stopped."
    }
}
finally {
    Pop-Location
}
