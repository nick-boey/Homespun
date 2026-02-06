@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the session pool')
param sessionPoolName string

@description('Container Apps environment ID')
param environmentId string

@description('Container image for agent workers')
param workerImage string

@description('User-assigned managed identity ID')
param identityId string

@description('Principal ID of the managed identity for RBAC')
param identityPrincipalId string

@description('GitHub token for worker containers')
@secure()
param githubToken string = ''

@description('Claude OAuth token for worker containers')
@secure()
param claudeOAuthToken string = ''

@description('Maximum concurrent sessions')
param maxConcurrentSessions int = 10

@description('Number of ready session instances to keep warm')
param readySessionInstances int = 2

resource sessionPool 'Microsoft.App/sessionPools@2025-07-01' = {
  name: sessionPoolName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    poolManagementType: 'Dynamic'
    dynamicPoolConfiguration: {
      lifecycleConfiguration: {
        lifecycleType: 'Timed'
        cooldownPeriodInSeconds: 300
      }
    }
    containerType: 'CustomContainer'
    scaleConfiguration: {
      maxConcurrentSessions: maxConcurrentSessions
      readySessionInstances: readySessionInstances
    }
    secrets: [
      {
        name: 'github-token'
        value: githubToken
      }
      {
        name: 'claude-oauth-token'
        value: claudeOAuthToken
      }
    ]
    customContainerTemplate: {
      containers: [
        {
          name: 'agent-worker'
          image: workerImage
          resources: {
            cpu: json('2.0')
            memory: '4Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'GitHub__Token'
              secretRef: 'github-token'
            }
            {
              name: 'CLAUDE_OAUTH_TOKEN'
              secretRef: 'claude-oauth-token'
            }
          ]
        }
      ]
      ingress: {
        targetPort: 8080
      }
    }
    managedIdentitySettings: [
      {
        identity: identityId
        lifecycle: 'Main'
      }
    ]
    sessionNetworkConfiguration: {
      status: 'EgressEnabled'
    }
  }
}

// Grant the managed identity Azure ContainerApps Session Executor role on the session pool
resource sessionExecutorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sessionPool.id, identityPrincipalId, 'Azure ContainerApps Session Executor')
  scope: sessionPool
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0fb8eba5-a2bb-4abe-b1c1-49dfad359bb0')
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output sessionPoolId string = sessionPool.id
output sessionPoolName string = sessionPool.name
output sessionPoolEndpoint string = sessionPool.properties.poolManagementEndpoint
