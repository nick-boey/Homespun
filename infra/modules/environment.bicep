@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the Container Apps environment')
param environmentName string

@description('Name of the Log Analytics workspace')
param logAnalyticsName string

@description('Storage account name for file share')
param storageAccountName string

@description('File share name')
param fileShareName string

@description('ACA infrastructure subnet ID for VNet integration')
param infrastructureSubnetId string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: true // Internal-only environment; Tailscale handles external access
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

// NFS storage mount for the environment (no account key needed - NFS uses network auth)
resource storageMount 'Microsoft.App/managedEnvironments/storages@2025-01-01' = {
  parent: environment
  name: 'homespun-storage'
  properties: {
    nfsAzureFile: {
      server: '${storageAccountName}.file.core.windows.net'
      shareName: '/${storageAccountName}/${fileShareName}'
      accessMode: 'ReadWrite'
    }
  }
}

output environmentId string = environment.id
output environmentName string = environment.name
output defaultDomain string = environment.properties.defaultDomain
output staticIp string = environment.properties.staticIp
output storageMountName string = storageMount.name
