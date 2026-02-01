# ============================================================================
# Homespun Mock Mode with Live Claude Sessions
# ============================================================================
#
# Starts Homespun in mock mode with live Claude Code sessions.
# This mode uses mock services for GitHub, Git, etc., but uses real Claude
# sessions targeting a test workspace directory. This is useful for testing
# the AskUserQuestion tool and other Claude interactions without needing
# a full GitHub integration.
#
# Usage:
#   .\mock-live.ps1                          # Start with default test-workspace
#   .\mock-live.ps1 -WorkingDirectory C:\..  # Start with custom working directory
#
# The application runs at: http://localhost:5095

param(
    [string]$WorkingDirectory
)

$ErrorActionPreference = "Stop"

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir

Write-Host "=== Homespun Mock Mode with Live Claude Sessions ===" -ForegroundColor Cyan
Write-Host

# Custom working directory from parameter
if ($WorkingDirectory) {
    $env:MockMode__LiveClaudeSessionsWorkingDirectory = $WorkingDirectory
    Write-Host "Using custom working directory: $WorkingDirectory" -ForegroundColor Cyan
} else {
    $DefaultDir = Join-Path $ProjectDir "test-workspace"
    Write-Host "Using default working directory: $DefaultDir" -ForegroundColor Cyan
}

Write-Host "Starting with mock services + live Claude sessions..." -ForegroundColor Cyan
Write-Host "Claude sessions will target the test workspace" -ForegroundColor Green
Write-Host

& dotnet run --project "$ProjectDir\src\Homespun" --launch-profile mock-live
