using './main.bicep'

param resourceGroupName = 'rg-homespun'
param location = 'australiaeast'
param vmSize = 'Standard_D4s_v3'
param adminUsername = 'homespun'
param baseName = 'homespun'
param osDiskSizeGb = 64

// Required: Set these before deploying
param adminSshPublicKey = readEnvironmentVariable('HOMESPUN_SSH_PUBLIC_KEY', '')

// Optional: Secrets passed via environment variables
param githubToken = readEnvironmentVariable('HSP_GITHUB_TOKEN', '')
param claudeCodeOAuthToken = readEnvironmentVariable('HSP_CLAUDE_CODE_OAUTH_TOKEN', '')
param tailscaleAuthKey = readEnvironmentVariable('HSP_TAILSCALE_AUTH_KEY', '')
param domainName = readEnvironmentVariable('HOMESPUN_DOMAIN_NAME', '')
