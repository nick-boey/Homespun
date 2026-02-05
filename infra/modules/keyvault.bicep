@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the Key Vault')
param keyVaultName string

@description('Principal ID of the managed identity that needs access')
param identityPrincipalId string

@description('GitHub token for GHCR access (optional)')
@secure()
param githubToken string = ''

@description('Claude OAuth token for Claude Code CLI (optional)')
@secure()
param claudeOAuthToken string = ''

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: false // Set to true for production
  }
}

// Grant the managed identity access to secrets
resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identityPrincipalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Store GitHub token if provided
resource githubTokenSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(githubToken)) {
  parent: keyVault
  name: 'github-token'
  properties: {
    value: githubToken
  }
}

// Store Claude OAuth token if provided
resource claudeTokenSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(claudeOAuthToken)) {
  parent: keyVault
  name: 'claude-oauth-token'
  properties: {
    value: claudeOAuthToken
  }
}

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
