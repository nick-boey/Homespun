#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys Homespun infrastructure to Azure.

.DESCRIPTION
    This script deploys the Homespun application infrastructure using Azure Bicep templates.
    It supports both development and production deployments.

.PARAMETER Environment
    The target environment (dev or prod). Defaults to 'dev'.

.PARAMETER ResourceGroup
    The Azure resource group name. Required.

.PARAMETER Location
    The Azure region for deployment. Defaults to 'australiaeast'.

.PARAMETER GitHubToken
    GitHub token for GHCR access. Optional.

.PARAMETER ClaudeOAuthToken
    Claude OAuth token for Claude Code CLI. Optional.

.PARAMETER WhatIf
    Shows what would happen without making changes.

.EXAMPLE
    ./deploy.ps1 -ResourceGroup "rg-homespun-dev" -Environment dev

.EXAMPLE
    ./deploy.ps1 -ResourceGroup "rg-homespun-prod" -Environment prod -GitHubToken $env:GITHUB_TOKEN -ClaudeOAuthToken $env:CLAUDE_OAUTH_TOKEN
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
    [securestring]$GitHubToken,

    [Parameter(Mandatory = $false)]
    [securestring]$ClaudeOAuthToken,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# Determine the script directory and repo root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$InfraDir = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent $InfraDir

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

# Check if resource group exists, create if not
Write-Host ""
Write-Host "Checking resource group..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroup | ConvertFrom-Json
if (-not $rgExists) {
    Write-Host "Creating resource group '$ResourceGroup' in '$Location'..." -ForegroundColor Yellow
    if (-not $WhatIf) {
        az group create --name $ResourceGroup --location $Location
    }
    else {
        Write-Host "[WhatIf] Would create resource group" -ForegroundColor Magenta
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
    Join-Path $InfraDir 'main.parameters.json'
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

# Add secrets if provided
if ($GitHubToken) {
    $DeploymentParams += '--parameters', "githubToken=$([System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($GitHubToken)))"
}
if ($ClaudeOAuthToken) {
    $DeploymentParams += '--parameters', "claudeOAuthToken=$([System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ClaudeOAuthToken)))"
}

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
    Write-Host ""
    Write-Host "===== What-If Complete =====" -ForegroundColor Green
}
