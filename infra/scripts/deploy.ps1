#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys Homespun infrastructure to Azure.

.DESCRIPTION
    This script deploys the Homespun application infrastructure using Azure Bicep templates.
    It supports both development and production deployments.

    Credentials (GitHub token, Claude OAuth token, Tailscale auth key) are resolved
    automatically from the same sources as run.ps1, in order:
      1. Explicit parameter (if provided)
      2. HSP_* environment variables (e.g. HSP_GITHUB_TOKEN)
      3. Standard environment variables (e.g. GITHUB_TOKEN)
      4. .NET user secrets (GitHub token only)
      5. .env file in the repository root

.PARAMETER Environment
    The target environment (dev or prod). Defaults to 'dev'.

.PARAMETER ResourceGroup
    The Azure resource group name. Required.

.PARAMETER Location
    The Azure region for deployment. Defaults to 'australiaeast'.

.PARAMETER GitHubToken
    GitHub token for GHCR access. Optional override; auto-discovered if not provided.

.PARAMETER ClaudeOAuthToken
    Claude OAuth token for Claude Code CLI. Optional override; auto-discovered if not provided.

.PARAMETER TailscaleAuthKey
    Tailscale auth key for VPN access. Optional override; auto-discovered if not provided.

.PARAMETER WhatIf
    Shows what would happen without making changes.

.EXAMPLE
    ./deploy.ps1 -ResourceGroup "rg-homespun-dev" -Environment dev

.EXAMPLE
    ./deploy.ps1 -ResourceGroup "rg-homespun-prod" -Environment prod

.EXAMPLE
    ./deploy.ps1 -ResourceGroup "rg-homespun-dev" -Environment dev -GitHubToken "ghp_..." -WhatIf
#>

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment = 'dev',

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$Location = 'australiaeast',

    [Parameter(Mandatory = $false)]
    [string]$GitHubToken,

    [Parameter(Mandatory = $false)]
    [string]$ClaudeOAuthToken,

    [Parameter(Mandatory = $false)]
    [string]$TailscaleAuthKey,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$createdTempRg = $false

# Determine the script directory and repo root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$InfraDir = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent $InfraDir

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

function Resolve-Credential {
    param(
        [string]$Name,
        [string]$ParamValue,
        [string[]]$EnvVarNames,
        [string]$EnvFilePath,
        [string]$UserSecretsKey
    )

    # 1. Explicit parameter
    if (-not [string]::IsNullOrWhiteSpace($ParamValue)) {
        Write-Host "      $Name : from parameter" -ForegroundColor Green
        return $ParamValue
    }

    # 2-3. Environment variables (HSP_* first, then standard)
    foreach ($varName in $EnvVarNames) {
        $value = [System.Environment]::GetEnvironmentVariable($varName)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            Write-Host "      $Name : from `$$varName" -ForegroundColor Green
            return $value
        }
    }

    # 4. .NET user secrets (if a key is specified)
    if (-not [string]::IsNullOrWhiteSpace($UserSecretsKey)) {
        $secretsPath = if ($env:APPDATA) {
            Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
        } else {
            Join-Path ([Environment]::GetFolderPath('UserProfile')) ".microsoft\usersecrets\$UserSecretsId\secrets.json"
        }
        if (Test-Path $secretsPath) {
            try {
                $secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
                $value = $secrets.$UserSecretsKey
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    Write-Host "      $Name : from user secrets" -ForegroundColor Green
                    return $value
                }
            }
            catch {
                # Continue to next fallback
            }
        }
    }

    # 5. .env file
    foreach ($varName in $EnvVarNames) {
        $value = Get-EnvFileValue -Key $varName -EnvFilePath $EnvFilePath
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            Write-Host "      $Name : from .env ($varName)" -ForegroundColor Green
            return $value
        }
    }

    Write-Host "      $Name : not found" -ForegroundColor DarkGray
    return ''
}

function Get-MaskedValue {
    param([string]$Value, [int]$ShowChars = 10)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '(empty)' }
    $show = [Math]::Min($ShowChars, $Value.Length)
    return $Value.Substring(0, $show) + '...'
}

# ============================================================================
# Main Script
# ============================================================================

Write-Host "===== Homespun Azure Deployment =====" -ForegroundColor Cyan
Write-Host "Environment: $Environment"
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Location: $Location"
Write-Host ""

# Check Azure CLI is installed and logged in
Write-Host "Checking Azure CLI..." -ForegroundColor Yellow
try {
    $account = az account show | ConvertFrom-Json
    Write-Host "Logged in as: $($account.user.name)" -ForegroundColor Green
    Write-Host "Subscription: $($account.name)" -ForegroundColor Green
}
catch {
    Write-Error "Please log in to Azure CLI: az login"
    exit 1
}

# Resolve credentials
Write-Host ""
Write-Host "Resolving credentials..." -ForegroundColor Yellow

$resolvedGitHubToken = Resolve-Credential `
    -Name 'GitHub token' `
    -ParamValue $GitHubToken `
    -EnvVarNames @('HSP_GITHUB_TOKEN', 'GITHUB_TOKEN') `
    -EnvFilePath $EnvFilePath `
    -UserSecretsKey 'GitHub:Token'

$resolvedClaudeOAuthToken = Resolve-Credential `
    -Name 'Claude OAuth token' `
    -ParamValue $ClaudeOAuthToken `
    -EnvVarNames @('HSP_CLAUDE_OAUTH_TOKEN', 'CLAUDE_CODE_OAUTH_TOKEN') `
    -EnvFilePath $EnvFilePath

$resolvedTailscaleAuthKey = Resolve-Credential `
    -Name 'Tailscale auth key' `
    -ParamValue $TailscaleAuthKey `
    -EnvVarNames @('HSP_TAILSCALE_AUTH_KEY', 'TAILSCALE_AUTH_KEY') `
    -EnvFilePath $EnvFilePath

Write-Host ""
if (-not [string]::IsNullOrWhiteSpace($resolvedGitHubToken)) {
    Write-Host "  GitHub token:      $(Get-MaskedValue $resolvedGitHubToken)" -ForegroundColor Green
}
if (-not [string]::IsNullOrWhiteSpace($resolvedClaudeOAuthToken)) {
    Write-Host "  Claude OAuth:      $(Get-MaskedValue $resolvedClaudeOAuthToken)" -ForegroundColor Green
}
if (-not [string]::IsNullOrWhiteSpace($resolvedTailscaleAuthKey)) {
    Write-Host "  Tailscale key:     $(Get-MaskedValue $resolvedTailscaleAuthKey 15)" -ForegroundColor Green
}

# Check if resource group exists, create if not
Write-Host ""
Write-Host "Checking resource group..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroup | ConvertFrom-Json
if (-not $rgExists) {
    Write-Host "Creating resource group '$ResourceGroup' in '$Location'..." -ForegroundColor Yellow
    az group create --name $ResourceGroup --location $Location
    if ($WhatIf) {
        $createdTempRg = $true
        Write-Host "[WhatIf] Resource group created temporarily for validation (will be cleaned up)" -ForegroundColor Magenta
    }
}
else {
    Write-Host "Resource group '$ResourceGroup' exists" -ForegroundColor Green
}

# Select parameter file
$ParameterFile = if ($Environment -eq 'prod') {
    Join-Path $InfraDir 'main.parameters.prod.json'
}
else {
    Join-Path $InfraDir 'main.parameters.dev.json'
}

Write-Host ""
Write-Host "Using parameter file: $ParameterFile" -ForegroundColor Yellow

# Build deployment command
$BicepFile = Join-Path $InfraDir 'main.bicep'
$DeploymentName = "homespun-$Environment-$(Get-Date -Format 'yyyyMMddHHmmss')"

$DeploymentParams = @(
    'deployment', 'group', 'create',
    '--resource-group', $ResourceGroup,
    '--template-file', $BicepFile,
    '--parameters', "@$ParameterFile"
)

# Add secrets to deployment parameters
$DeploymentParams += '--parameters', "githubToken=$resolvedGitHubToken"
$DeploymentParams += '--parameters', "claudeOAuthToken=$resolvedClaudeOAuthToken"
$DeploymentParams += '--parameters', "tailscaleAuthKey=$resolvedTailscaleAuthKey"

$DeploymentParams += '--name', $DeploymentName

if ($WhatIf) {
    $DeploymentParams += '--what-if'
}

# Run deployment
Write-Host ""
Write-Host "Starting deployment: $DeploymentName" -ForegroundColor Yellow
Write-Host "Command: az $($DeploymentParams -join ' ')" -ForegroundColor Gray
Write-Host ""

$result = & az @DeploymentParams

if ($LASTEXITCODE -ne 0) {
    Write-Error "Deployment failed"
    exit 1
}

if (-not $WhatIf) {
    Write-Host ""
    Write-Host "===== Deployment Complete =====" -ForegroundColor Green
    Write-Host ""

    # Get outputs
    $outputs = az deployment group show `
        --resource-group $ResourceGroup `
        --name $DeploymentName `
        --query 'properties.outputs' | ConvertFrom-Json

    Write-Host "Container App URL: $($outputs.containerAppUrl.value)" -ForegroundColor Cyan
    Write-Host "Container App FQDN: $($outputs.containerAppFqdn.value)" -ForegroundColor Cyan
    Write-Host "Key Vault Name: $($outputs.keyVaultName.value)" -ForegroundColor Cyan
    Write-Host "Storage Account: $($outputs.storageAccountName.value)" -ForegroundColor Cyan

    if ($outputs.sessionPoolName.value -ne 'N/A') {
        Write-Host "Session Pool: $($outputs.sessionPoolName.value)" -ForegroundColor Cyan
    }
}
else {
    if ($createdTempRg) {
        Write-Host ""
        Write-Host "Cleaning up temporary resource group '$ResourceGroup'..." -ForegroundColor Yellow
        az group delete --name $ResourceGroup --yes --no-wait
        Write-Host "Resource group deletion initiated (--no-wait)" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "===== What-If Complete =====" -ForegroundColor Green
}
