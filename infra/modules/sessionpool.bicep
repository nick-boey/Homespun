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

@description('Storage mount name in the environment')
param storageMountName string

@description('Maximum concurrent sessions')
param maxConcurrentSessions int = 10

@description('Number of ready session instances to keep warm')
param readySessionInstances int = 2

resource sessionPool 'Microsoft.App/sessionPools@2024-02-02-preview' = {
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
      executionType: 'Timed'
      cooldownPeriodInSeconds: 300 // 5 minutes
    }
    containerType: 'CustomContainer'
    scaleConfiguration: {
      maxConcurrentSessions: maxConcurrentSessions
      readySessionInstances: readySessionInstances
    }
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
          ]
          volumeMounts: [
            {
              volumeName: 'data-volume'
              mountPath: '/data'
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
    }
    sessionNetworkConfiguration: {
      status: 'EgressEnabled'
    }
  }
}

output sessionPoolId string = sessionPool.id
output sessionPoolName string = sessionPool.name
output sessionPoolEndpoint string = sessionPool.properties.poolManagementEndpoint
