#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs Homespun with mock services and demo data using dotnet run.

.DESCRIPTION
    This script runs Homespun with mock services and demo data.
    No external dependencies (GitHub API, Claude API, etc.) are required.
    Useful for UI development and testing.

.PARAMETER Port
    Override the port from the launch profile.

.EXAMPLE
    .\mock.ps1
    Runs the application in mock mode

.EXAMPLE
    .\mock.ps1 -Port 8080
    Runs the application in mock mode on http://localhost:8080

.NOTES
    Mock data includes demo projects, features, and issues.
#>

#Requires -Version 7.0

param(
    [int]$Port = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory and project root
$ScriptDir = $PSScriptRoot
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ProjectPath = Join-Path $ProjectRoot "src" "Homespun.Server" "Homespun.Server.csproj"

Write-Host "=== Homespun Mock Mode ===" -ForegroundColor Cyan
Write-Host "Running with mock services and demo data..." -ForegroundColor Cyan
Write-Host ""

# Build the dotnet run command
$dotnetArgs = @(
    "run"
    "--project", $ProjectPath
    "--launch-profile", "mock"
)

if ($Port -ne 0) {
    $dotnetArgs += "--urls"
    $dotnetArgs += "http://localhost:$Port"
}

& dotnet @dotnetArgs
