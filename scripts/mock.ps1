#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs Homespun with mock services and demo data.

.DESCRIPTION
    Starts both the backend (dotnet) and frontend (Vite) dev servers.
    Logs are captured to logs/mock-backend.log and logs/mock-frontend.log.
    No external dependencies (GitHub API, Claude API, etc.) are required.

.PARAMETER Port
    Override the backend port (default from launch profile).

.PARAMETER Foreground
    Run backend only in foreground with output to terminal (original behavior).

.EXAMPLE
    .\mock.ps1
    Runs both backend and frontend with logs captured to files.

.EXAMPLE
    .\mock.ps1 -Port 8080
    Runs on a custom backend port.

.EXAMPLE
    .\mock.ps1 -Foreground
    Runs backend only in foreground with output to terminal.

.NOTES
    Mock data includes demo projects, features, and issues.

    WARNING: This script runs long-lived server processes. If running as a
    background shell in Claude Code, do NOT use KillShell on this process.
    Killing this shell may terminate your entire session. Instead, use
    Stop-Process with the process PIDs directly.
#>

#Requires -Version 7.0

param(
    [int]$Port = 0,
    [switch]$Foreground
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory and project root
$ScriptDir = $PSScriptRoot
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ProjectPath = Join-Path $ProjectRoot "src" "Homespun.Server" "Homespun.Server.csproj"
$WebDir = Join-Path $ProjectRoot "src" "Homespun.Web"
$LogDir = Join-Path $ProjectRoot "logs"

# Build the dotnet run command args
$dotnetArgs = @(
    "run"
    "--project", $ProjectPath
    "--launch-profile", "mock"
)

if ($Port -ne 0) {
    $dotnetArgs += "--urls"
    $dotnetArgs += "http://localhost:$Port"
}

# Foreground mode: original behavior (backend only, output to terminal)
if ($Foreground) {
    Write-Host "=== Homespun Mock Mode (foreground) ===" -ForegroundColor Cyan
    Write-Host "Running backend only with output to terminal..." -ForegroundColor Cyan
    Write-Host "WARNING: Do not use KillShell on this process - use 'Stop-Process -Name dotnet' instead" -ForegroundColor Yellow
    Write-Host ""
    & dotnet @dotnetArgs
    return
}

# Background mode: run both backend and frontend with logs captured to files
Write-Host "=== Homespun Mock Mode ===" -ForegroundColor Cyan
Write-Host "Starting backend and frontend servers..." -ForegroundColor Cyan
Write-Host "WARNING: Do not use KillShell on this process - use 'Stop-Process -Id <pid>' instead" -ForegroundColor Yellow
Write-Host ""

# Set up log directory
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir | Out-Null
}

$BackendLog = Join-Path $LogDir "mock-backend.log"
$FrontendLog = Join-Path $LogDir "mock-frontend.log"

# Start backend
Write-Host "Starting backend server..." -ForegroundColor Cyan
$backendProcess = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs -RedirectStandardOutput $BackendLog -RedirectStandardError (Join-Path $LogDir "mock-backend-error.log") -PassThru -NoNewWindow
Write-Host "Backend started (PID: $($backendProcess.Id))" -ForegroundColor Green

# Start frontend
Write-Host "Starting frontend dev server..." -ForegroundColor Cyan
$frontendProcess = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "npm run dev" -WorkingDirectory $WebDir -RedirectStandardOutput $FrontendLog -RedirectStandardError (Join-Path $LogDir "mock-frontend-error.log") -PassThru -NoNewWindow
Write-Host "Frontend started (PID: $($frontendProcess.Id))" -ForegroundColor Green

Write-Host ""
Write-Host "Both servers are running!" -ForegroundColor Green
Write-Host "Backend log:  $BackendLog" -ForegroundColor Cyan
Write-Host "Frontend log: $FrontendLog" -ForegroundColor Cyan
Write-Host ""
Write-Host "Use 'Get-Content $BackendLog -Wait' to follow backend logs" -ForegroundColor Cyan
Write-Host "Use 'Get-Content $FrontendLog -Wait' to follow frontend logs" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press Ctrl+C to stop both servers" -ForegroundColor Yellow

# Cleanup on exit
try {
    # Wait for either process to exit
    while (-not $backendProcess.HasExited -and -not $frontendProcess.HasExited) {
        Start-Sleep -Milliseconds 500
    }

    if ($backendProcess.HasExited) {
        Write-Host "Backend process exited (code: $($backendProcess.ExitCode)). Stopping frontend..." -ForegroundColor Yellow
    } else {
        Write-Host "Frontend process exited (code: $($frontendProcess.ExitCode)). Stopping backend..." -ForegroundColor Yellow
    }
} finally {
    # Kill remaining processes
    if (-not $backendProcess.HasExited) {
        Stop-Process -Id $backendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if (-not $frontendProcess.HasExited) {
        Stop-Process -Id $frontendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Servers stopped." -ForegroundColor Cyan
}
