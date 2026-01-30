#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and runs Homespun in a container with mock mode enabled.

.DESCRIPTION
    This script runs Homespun with mock services and demo data in a container.
    No external dependencies (GitHub API, Claude API, etc.) are required.
    Useful for UI development and testing.

.PARAMETER Port
    The host port to expose the container on (default: 5095).

.PARAMETER Interactive
    Run in interactive mode (foreground).

.PARAMETER Detach
    Run in detached mode (background). This is the default.

.PARAMETER Stop
    Stop the mock container.

.PARAMETER Logs
    View container logs.

.PARAMETER ContainerName
    Override the container name (default: homespun-mock).

.EXAMPLE
    .\mock.ps1
    Runs the application in mock mode at http://localhost:5095

.EXAMPLE
    .\mock.ps1 -Port 8080
    Runs the application in mock mode at http://localhost:8080

.EXAMPLE
    .\mock.ps1 -Interactive
    Runs in interactive mode (foreground).

.EXAMPLE
    .\mock.ps1 -Stop
    Stops the mock container.

.EXAMPLE
    .\mock.ps1 -Logs
    Views the container logs.

.NOTES
    URL: http://localhost:5095 (or custom port)
    Mock data includes demo projects, features, and issues.
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')]
    [int]$Port = 5095,

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
    [string]$ContainerName = "homespun-mock"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = $PSScriptRoot

Write-Host "=== Homespun Mock Mode (Container) ===" -ForegroundColor Cyan
Write-Host "Building and running with mock services and demo data..." -ForegroundColor Cyan
Write-Host ""

# Build arguments for run.ps1
$runArgs = @{
    Local = $true
    MockMode = $true
    Port = $Port
    ContainerName = $ContainerName
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
    $runArgs.Stop = $true
}

if ($Logs) {
    $runArgs.Remove('Local')
    $runArgs.Remove('MockMode')
    $runArgs.Remove('Port')
    $runArgs.Logs = $true
}

# Call run.ps1 with the mock mode flags
& "$ScriptDir\run.ps1" @runArgs
