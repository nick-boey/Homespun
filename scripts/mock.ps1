#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and runs Homespun in a container with mock mode enabled.

.DESCRIPTION
    This script runs Homespun with mock services and demo data in a container.
    No external dependencies (GitHub API, Claude API, etc.) are required.
    Useful for UI development and testing.

    Each git worktree automatically gets its own container, port, and data
    directory based on a hash of the worktree path. This allows multiple
    agents to run mock mode concurrently without conflicts.

.PARAMETER Port
    Override the host port. Default is auto-computed from worktree (15000-15999).

.PARAMETER Interactive
    Run in interactive mode (foreground).

.PARAMETER Detach
    Run in detached mode (background). This is the default.

.PARAMETER Stop
    Stop the mock container for this worktree.

.PARAMETER Logs
    View container logs.

.PARAMETER ContainerName
    Override the container name. Default is homespun-mock-<worktree-hash>.

.PARAMETER DataDir
    Override the data directory. Default is ~/.homespun-container/mock-<worktree-hash>.

.EXAMPLE
    .\mock.ps1
    Runs the application in mock mode with auto-configured worktree settings.

.EXAMPLE
    .\mock.ps1 -Port 8080
    Runs the application in mock mode at http://localhost:8080

.EXAMPLE
    .\mock.ps1 -Interactive
    Runs in interactive mode (foreground).

.EXAMPLE
    .\mock.ps1 -Stop
    Stops the mock container for this worktree.

.EXAMPLE
    .\mock.ps1 -Logs
    Views the container logs.

.NOTES
    Worktree Isolation:
        Container name: homespun-mock-<hash>
        Port: 15000-15999 (computed from worktree path)
        Data dir: ~/.homespun-container/mock-<hash>

    Mock data includes demo projects, features, and issues.
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')]
    [int]$Port = 0,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Interactive,

    [Parameter(ParameterSetName = 'Run')]
    [switch]$Detach,

    [Parameter(ParameterSetName = 'Stop')]
    [switch]$Stop,

    [Parameter(ParameterSetName = 'Logs')]
    [switch]$Logs,

    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'Stop')]
    [Parameter(ParameterSetName = 'Logs')]
    [string]$ContainerName,

    [Parameter(ParameterSetName = 'Run')]
    [string]$DataDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot

# ============================================================================
# Worktree Detection and Auto-Configuration
# ============================================================================

function Get-WorktreeRoot {
    try {
        $root = git rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0 -and $root) {
            return $root.Trim()
        }
    }
    catch {
        # Ignore errors
    }
    # Fallback to script parent directory
    return (Resolve-Path (Join-Path $ScriptDir "..")).Path
}

function Get-WorktreeHash {
    param([string]$Path)

    # Compute MD5 hash and take first 8 characters
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Path)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hashBytes = $md5.ComputeHash($bytes)
    $hash = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
    return $hash.Substring(0, 8)
}

function Get-PortFromHash {
    param([string]$Hash)

    # Convert first 4 hex chars to decimal and mod 1000
    $hexVal = $Hash.Substring(0, 4)
    $decimalVal = [Convert]::ToInt32($hexVal, 16)
    $port = 15000 + ($decimalVal % 1000)
    return $port
}

function Test-PortAvailable {
    param([int]$Port)

    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        $listener.Stop()
        return $true
    }
    catch {
        return $false
    }
}

function Find-AvailablePort {
    param([int]$StartPort)

    $maxAttempts = 50
    $port = $StartPort

    for ($i = 0; $i -lt $maxAttempts; $i++) {
        if (Test-PortAvailable -Port $port) {
            return $port
        }
        $port++
        # Wrap around within range
        if ($port -ge 16000) {
            $port = 15000
        }
    }

    # Return start port if nothing found (let docker fail with clear error)
    return $StartPort
}

function Get-WorktreeIdentifier {
    $worktreeRoot = Get-WorktreeRoot
    return Get-WorktreeHash -Path $worktreeRoot
}

function Get-WorktreePort {
    param([string]$Hash)

    $computedPort = Get-PortFromHash -Hash $Hash
    return Find-AvailablePort -StartPort $computedPort
}

# ============================================================================
# Main Script
# ============================================================================

# Generate worktree-specific defaults
$WorktreeId = Get-WorktreeIdentifier
$WorktreeRoot = Get-WorktreeRoot

# Apply overrides or use auto-configured values
$effectivePort = if ($Port -ne 0) { $Port } else { Get-WorktreePort -Hash $WorktreeId }
$effectiveContainerName = if ([string]::IsNullOrWhiteSpace($ContainerName)) { "homespun-mock-$WorktreeId" } else { $ContainerName }
$homeDir = [Environment]::GetFolderPath('UserProfile')
$effectiveDataDir = if ([string]::IsNullOrWhiteSpace($DataDir)) { Join-Path $homeDir ".homespun-container" "mock-$WorktreeId" } else { $DataDir }

Write-Host "=== Homespun Mock Mode (Container) ===" -ForegroundColor Cyan
Write-Host "Building and running with mock services and demo data..." -ForegroundColor Cyan
Write-Host ""
Write-Host "Worktree Configuration:" -ForegroundColor Cyan
Write-Host "  Worktree:    $WorktreeRoot"
Write-Host "  Identifier:  $WorktreeId"
Write-Host "  Container:   $effectiveContainerName"
Write-Host "  Port:        $effectivePort"
Write-Host "  Data dir:    $effectiveDataDir"
Write-Host ""

# Build arguments for run.ps1
$runArgs = @{
    Local = $true
    MockMode = $true
    NoTailscale = $true
    Port = $effectivePort
    ContainerName = $effectiveContainerName
    DataDir = $effectiveDataDir
}

if ($Interactive) {
    $runArgs.Interactive = $true
}

if ($Detach) {
    $runArgs.Detach = $true
}

if ($Stop) {
    $runArgs.Remove('Local')
    $runArgs.Remove('MockMode')
    $runArgs.Remove('Port')
    $runArgs.Remove('DataDir')
    $runArgs.Stop = $true
}

if ($Logs) {
    $runArgs.Remove('Local')
    $runArgs.Remove('MockMode')
    $runArgs.Remove('Port')
    $runArgs.Remove('DataDir')
    $runArgs.Logs = $true
}

# Call run.ps1 with the mock mode flags
& "$ScriptDir\run.ps1" @runArgs
