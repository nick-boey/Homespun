@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the worker Container App')
param workerAppName string

@description('Container Apps environment ID')
param environmentId string

@description('User-assigned managed identity ID')
param identityId string

@description('Container image for the worker')
param workerImage string

@description('Key Vault URI for secrets')
param keyVaultUri string

@description('Storage mount name in the environment')
param storageMountName string

@description('Maximum concurrent sessions (used for scaling)')
param maxConcurrentSessions int = 10

@description('Deployment timestamp for revision suffix to force new revisions')
param deploymentTimestamp string

resource workerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: workerAppName
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
        external: false // Only accessible within the ACA environment
        targetPort: 8080
        transport: 'auto'
        allowInsecure: true
      }
      secrets: [
        {
          name: 'github-token'
          keyVaultUrl: '${keyVaultUri}secrets/github-token'
          identity: identityId
        }
        {
          name: 'claude-oauth-token'
          keyVaultUrl: '${keyVaultUri}secrets/claude-oauth-token'
          identity: identityId
        }
      ]
      registries: [] // Public GHCR doesn't need auth
    }
    template: {
      revisionSuffix: deploymentTimestamp
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
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
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
                path: '/api/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/api/health'
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
          storageType: 'NfsAzureFile'
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: maxConcurrentSessions
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '5' // Scale up when sessions increase
              }
            }
          }
        ]
      }
    }
  }
}

output workerAppId string = workerApp.id
output workerAppName string = workerApp.name
output workerAppFqdn string = workerApp.properties.configuration.ingress.fqdn
output workerAppUrl string = 'http://${workerApp.properties.configuration.ingress.fqdn}'
