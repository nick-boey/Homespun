@description('Location for all resources')
param location string = resourceGroup().location

@description('Base name for resources')
param baseName string = 'homespun'

@description('Environment name suffix (e.g., dev, prod)')
param environmentSuffix string = 'dev'

@description('Container image for main app')
param mainAppImage string = 'ghcr.io/nick-boey/homespun:latest'

@description('Container image for agent workers')
param workerImage string = 'ghcr.io/nick-boey/homespun-worker:latest'

@description('Agent execution mode')
@allowed(['Local', 'Docker', 'AzureContainerApps'])
param agentExecutionMode string = 'Local'

@description('GitHub token for GHCR access')
@secure()
param githubToken string = ''

@description('Claude OAuth token for Claude Code CLI')
@secure()
param claudeOAuthToken string = ''

@description('Tailscale auth key for VPN access')
@secure()
param tailscaleAuthKey string = ''

@description('Maximum concurrent agent sessions')
param maxConcurrentSessions int = 10

@description('Number of warm agent instances')
param readySessionInstances int = 2

@description('Storage network ACL default action. Use Deny only with VNet/private endpoint.')
@allowed(['Allow', 'Deny'])
param storageNetworkDefaultAction string = 'Allow'

@description('Deployment timestamp used to force new container revisions')
param deploymentTimestamp string = utcNow('yyyyMMddHHmmss')

// Resource naming
var resourceSuffix = '${baseName}-${environmentSuffix}'
var identityName = 'id-${resourceSuffix}'
var keyVaultName = 'kv-${baseName}${environmentSuffix}' // Key Vault names must be globally unique
var storageAccountName = 'st${baseName}${environmentSuffix}' // Storage account names must be globally unique
var logAnalyticsName = 'log-${resourceSuffix}'
var environmentName = 'cae-${resourceSuffix}'
var containerAppName = 'ca-${resourceSuffix}'
var sessionPoolName = 'sp-${resourceSuffix}'

// Create managed identity
module identity 'modules/identity.bicep' = {
  name: 'identity-deployment'
  params: {
    location: location
    identityName: identityName
  }
}

// Create Key Vault with secrets
module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-deployment'
  params: {
    location: location
    keyVaultName: keyVaultName
    identityPrincipalId: identity.outputs.identityPrincipalId
    githubToken: githubToken
    claudeOAuthToken: claudeOAuthToken
    tailscaleAuthKey: tailscaleAuthKey
  }
}

// Create storage account with file share
module storage 'modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    location: location
    storageAccountName: storageAccountName
    networkDefaultAction: storageNetworkDefaultAction
  }
}

// Create Container Apps environment
module environment 'modules/environment.bicep' = {
  name: 'environment-deployment'
  params: {
    location: location
    environmentName: environmentName
    logAnalyticsName: logAnalyticsName
    storageAccountName: storage.outputs.storageAccountName
    storageAccountKey: storage.outputs.storageAccountKey
    fileShareName: storage.outputs.fileShareName
  }
}

// Create session pool for agent workers (only if using AzureContainerApps mode)
module sessionPool 'modules/sessionpool.bicep' = if (agentExecutionMode == 'AzureContainerApps') {
  name: 'sessionpool-deployment'
  params: {
    location: location
    sessionPoolName: sessionPoolName
    environmentId: environment.outputs.environmentId
    workerImage: workerImage
    identityId: identity.outputs.identityId
    identityPrincipalId: identity.outputs.identityPrincipalId
    githubToken: githubToken
    claudeOAuthToken: claudeOAuthToken
    maxConcurrentSessions: maxConcurrentSessions
    readySessionInstances: readySessionInstances
  }
}

// Create main Container App
module containerApp 'modules/containerapp.bicep' = {
  name: 'containerapp-deployment'
  params: {
    location: location
    containerAppName: containerAppName
    environmentId: environment.outputs.environmentId
    identityId: identity.outputs.identityId
    containerImage: mainAppImage
    keyVaultUri: keyVault.outputs.keyVaultUri
    storageMountName: environment.outputs.storageMountName
    agentExecutionMode: agentExecutionMode
    sessionPoolName: agentExecutionMode == 'AzureContainerApps' ? sessionPoolName : ''
    deploymentTimestamp: deploymentTimestamp
  }
}

// Outputs
output resourceGroupName string = resourceGroup().name
output containerAppUrl string = containerApp.outputs.containerAppUrl
output containerAppFqdn string = containerApp.outputs.containerAppFqdn
output identityClientId string = identity.outputs.identityClientId
output keyVaultName string = keyVault.outputs.keyVaultName
output storageAccountName string = storage.outputs.storageAccountName
output sessionPoolName string = agentExecutionMode == 'AzureContainerApps' ? sessionPool.outputs.sessionPoolName : 'N/A'
