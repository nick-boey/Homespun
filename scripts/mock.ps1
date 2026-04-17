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

.PARAMETER WithWorker
    Bring up the docker-compose `worker` service, wait for its healthcheck,
    then start the backend in SingleContainer agent-execution mode pointed at
    http://localhost:$env:WORKER_HOST_PORT (default 8081). Dev-only; enforces
    one active Claude Agent SDK session at a time. Requires
    $env:CLAUDE_CODE_OAUTH_TOKEN to be set.

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
    [switch]$Foreground,
    [switch]$WithWorker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Get script directory and project root
$ScriptDir = $PSScriptRoot
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ProjectPath = Join-Path $ProjectRoot "src" "Homespun.Server" "Homespun.Server.csproj"
$WebDir = Join-Path $ProjectRoot "src" "Homespun.Web"
$LogDir = Join-Path $ProjectRoot "logs"

$WorkerHostPort = if ($env:WORKER_HOST_PORT) { $env:WORKER_HOST_PORT } else { "8081" }

function Stop-ComposeWorker {
    if (-not $WithWorker) { return }
    Write-Host "Stopping docker-compose worker..." -ForegroundColor Cyan
    Push-Location $ProjectRoot
    try {
        & docker compose stop worker | Out-Null
    } catch {
        Write-Host "Failed to stop worker: $_" -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
}

if ($WithWorker) {
    if (-not $env:CLAUDE_CODE_OAUTH_TOKEN) {
        Write-Host "ERROR: CLAUDE_CODE_OAUTH_TOKEN is not set; aborting." -ForegroundColor Red
        Write-Host "       --WithWorker boots a real Claude Agent SDK worker which requires authentication." -ForegroundColor Red
        exit 1
    }

    Write-Host "Starting docker-compose worker on host port $WorkerHostPort..." -ForegroundColor Cyan
    Push-Location $ProjectRoot
    try {
        $env:WORKER_HOST_PORT = $WorkerHostPort
        & docker compose up -d worker
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose up -d worker failed"
        }
    } finally {
        Pop-Location
    }

    Write-Host "Waiting for worker /api/health on http://localhost:$WorkerHostPort..." -ForegroundColor Cyan
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri "http://localhost:$WorkerHostPort/api/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $ready) {
        Write-Host "Worker did not become healthy within 30s; aborting." -ForegroundColor Yellow
        Stop-ComposeWorker
        exit 1
    }
    Write-Host "Worker is healthy." -ForegroundColor Green

    $env:AgentExecution__Mode = "SingleContainer"
    $env:AgentExecution__SingleContainer__WorkerUrl = "http://localhost:$WorkerHostPort"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    # The `mock` launch profile enables HOMESPUN_MOCK_MODE which bypasses the
    # real agent-execution registration entirely. With --WithWorker the point
    # is to hit the real SingleContainer shim, so force mock mode off.
    $env:HOMESPUN_MOCK_MODE = "false"
    $env:MockMode__Enabled = "false"
}

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
    try {
        Write-Host "=== Homespun Mock Mode (foreground) ===" -ForegroundColor Cyan
        Write-Host "Running backend only with output to terminal..." -ForegroundColor Cyan
        Write-Host "WARNING: Do not use KillShell on this process - use 'Stop-Process -Name dotnet' instead" -ForegroundColor Yellow
        Write-Host ""
        & dotnet @dotnetArgs
    } finally {
        Stop-ComposeWorker
    }
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
    Stop-ComposeWorker
    Write-Host "Servers stopped." -ForegroundColor Cyan
}
