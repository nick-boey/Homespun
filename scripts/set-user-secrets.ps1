# ============================================================================
# One-shot migration: .env -> dotnet user-secrets (Homespun.AppHost)
# ============================================================================
#
# Reads GITHUB_TOKEN and CLAUDE_CODE_OAUTH_TOKEN from .env at repo root and
# stores them under Parameters:github-token / Parameters:claude-oauth-token
# in the user-secrets store scoped to src/Homespun.AppHost so that Aspire
# resolves them at dev-run time.
#
# Idempotent: missing .env or blank key -> warn and skip; does not clear
# existing user-secrets.

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$EnvFile = Join-Path $ProjectRoot ".env"
$AppHostProject = Join-Path $ProjectRoot "src/Homespun.AppHost"

if (-not (Test-Path $EnvFile)) {
    Write-Warning "$EnvFile not found - nothing to migrate."
    exit 0
}

function Get-EnvValue {
    param([string]$Key)
    $line = Get-Content $EnvFile |
        Where-Object { $_ -match "^\s*$([Regex]::Escape($Key))=" } |
        Select-Object -Last 1
    if (-not $line) { return "" }
    $value = $line -replace "^\s*$([Regex]::Escape($Key))=", ""
    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
        $value = $value.Trim('"')
    }
    elseif ($value.StartsWith("'") -and $value.EndsWith("'")) {
        $value = $value.Trim("'")
    }
    return $value
}

function Set-Secret {
    param(
        [string]$Key,
        [string]$Value,
        [string]$SecretName
    )
    if ([string]::IsNullOrEmpty($Value)) {
        Write-Warning "$Key is blank in .env - leaving user-secret '$SecretName' untouched."
        return
    }
    dotnet user-secrets set $SecretName $Value --project $AppHostProject | Out-Null
    Write-Host "OK:   $SecretName set from $Key."
}

$githubToken = Get-EnvValue -Key "GITHUB_TOKEN"
$claudeToken = Get-EnvValue -Key "CLAUDE_CODE_OAUTH_TOKEN"

Set-Secret -Key "GITHUB_TOKEN" -Value $githubToken -SecretName "Parameters:github-token"
Set-Secret -Key "CLAUDE_CODE_OAUTH_TOKEN" -Value $claudeToken -SecretName "Parameters:claude-oauth-token"
