@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the Container App')
param containerAppName string

@description('Container Apps environment ID')
param environmentId string

@description('User-assigned managed identity ID')
param identityId string

@description('Container image to deploy')
param containerImage string

@description('Key Vault URI for secrets')
param keyVaultUri string

@description('Storage mount name in the environment')
param storageMountName string

@description('GitHub token secret name in Key Vault')
param githubTokenSecretName string = 'github-token'

@description('Agent execution mode')
@allowed(['Local', 'Docker', 'AzureContainerApps'])
param agentExecutionMode string = 'Local'

@description('Session pool name for Azure Container Apps mode')
param sessionPoolName string = ''

@description('Azure subscription ID')
param subscriptionId string = subscription().subscriptionId

@description('Resource group name')
param resourceGroupName string = resourceGroup().name

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false // Tailscale handles all inbound access
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'github-token'
          keyVaultUrl: '${keyVaultUri}secrets/${githubTokenSecretName}'
          identity: identityId
        }
        {
          name: 'claude-oauth-token'
          keyVaultUrl: '${keyVaultUri}secrets/claude-oauth-token'
          identity: identityId
        }
        {
          name: 'tailscale-auth-key'
          keyVaultUrl: '${keyVaultUri}secrets/tailscale-auth-key'
          identity: identityId
        }
      ]
      registries: [] // Public GHCR doesn't need auth
    }
    template: {
      containers: [
        {
          name: 'homespun'
          image: containerImage
          resources: {
            cpu: json('2.0')
            memory: '4Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'HOMESPUN_DATA_PATH'
              value: '/data/homespun-data.json'
            }
            {
              name: 'GitHub__Token'
              secretRef: 'github-token'
            }
            {
              name: 'CLAUDE_OAUTH_TOKEN'
              secretRef: 'claude-oauth-token'
            }
            {
              name: 'TAILSCALE_AUTH_KEY'
              secretRef: 'tailscale-auth-key'
            }
            {
              name: 'TS_HOSTNAME'
              value: 'homespun'
            }
            {
              name: 'AgentExecution__Mode'
              value: agentExecutionMode
            }
            {
              name: 'AgentExecution__AzureContainerApps__SubscriptionId'
              value: subscriptionId
            }
            {
              name: 'AgentExecution__AzureContainerApps__ResourceGroup'
              value: resourceGroupName
            }
            {
              name: 'AgentExecution__AzureContainerApps__SessionPoolName'
              value: sessionPoolName
            }
          ]
          volumeMounts: [
            {
              volumeName: 'data-volume'
              mountPath: '/data'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'data-volume'
          storageName: storageMountName
          storageType: 'AzureFile'
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}

output containerAppId string = containerApp.id
output containerAppName string = containerApp.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
