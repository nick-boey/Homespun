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

@description('Maximum concurrent agent sessions (controls worker container app scaling)')
param maxConcurrentSessions int = 10

@description('Storage network ACL default action. Use Deny with VNet/private endpoint for NFS.')
@allowed(['Allow', 'Deny'])
param storageNetworkDefaultAction string = 'Deny'

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
var workerAppName = 'ca-worker-${resourceSuffix}'
var vnetName = 'vnet-${resourceSuffix}'
var storageEndpointName = 'pe-st-${resourceSuffix}'

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

// Create VNet with subnets for ACA and storage private endpoint
module network 'modules/network.bicep' = {
  name: 'network-deployment'
  params: {
    location: location
    vnetName: vnetName
  }
}

// Create storage account with NFS file share
module storage 'modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    location: location
    storageAccountName: storageAccountName
    networkDefaultAction: storageNetworkDefaultAction
  }
}

// Create private endpoint for storage (NFS requires private endpoint access)
module storageEndpoint 'modules/storage-endpoint.bicep' = {
  name: 'storage-endpoint-deployment'
  params: {
    location: location
    privateEndpointName: storageEndpointName
    storageAccountId: storage.outputs.storageAccountId
    subnetId: network.outputs.storageSubnetId
    vnetId: network.outputs.vnetId
  }
}

// Create Container Apps environment with VNet integration
module environment 'modules/environment.bicep' = {
  name: 'environment-deployment'
  params: {
    location: location
    environmentName: environmentName
    logAnalyticsName: logAnalyticsName
    storageAccountName: storage.outputs.storageAccountName
    fileShareName: storage.outputs.fileShareName
    infrastructureSubnetId: network.outputs.acaSubnetId
  }
}

// Create worker container app for agent sessions (only if using AzureContainerApps mode)
module workerApp 'modules/worker-containerapp.bicep' = if (agentExecutionMode == 'AzureContainerApps') {
  name: 'worker-containerapp-deployment'
  params: {
    location: location
    workerAppName: workerAppName
    environmentId: environment.outputs.environmentId
    identityId: identity.outputs.identityId
    workerImage: workerImage
    keyVaultUri: keyVault.outputs.keyVaultUri
    storageMountName: environment.outputs.storageMountName
    maxConcurrentSessions: maxConcurrentSessions
    deploymentTimestamp: deploymentTimestamp
  }
}

// Role assignment: Contributor on resource group for dynamic Container App creation
// The managed identity needs to create/delete Container Apps at runtime
resource contributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (agentExecutionMode == 'AzureContainerApps') {
  name: guid(resourceGroup().id, identity.outputs.identityPrincipalId, 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c') // Contributor
    principalId: identity.outputs.identityPrincipalId
    principalType: 'ServicePrincipal'
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
    workerAppFqdn: agentExecutionMode == 'AzureContainerApps' ? workerApp.outputs.workerAppFqdn : ''
    workerImage: workerImage
    resourceGroupName: resourceGroup().name
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
output workerAppFqdn string = agentExecutionMode == 'AzureContainerApps' ? workerApp.outputs.workerAppFqdn : 'N/A'
