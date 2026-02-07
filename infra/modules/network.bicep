@description('Location for all resources')
param location string = resourceGroup().location

@description('Name of the virtual network')
param vnetName string

@description('VNet address space')
param addressPrefix string = '10.0.0.0/16'

@description('ACA infrastructure subnet address prefix (minimum /23 required)')
param acaSubnetPrefix string = '10.0.0.0/23'

@description('Storage private endpoint subnet address prefix')
param storageSubnetPrefix string = '10.0.2.0/24'

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
    subnets: [
      {
        name: 'aca-infra'
        properties: {
          addressPrefix: acaSubnetPrefix
          // ACA requires delegation for managed environments
          delegations: [
            {
              name: 'Microsoft.App.environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'storage-pe'
        properties: {
          addressPrefix: storageSubnetPrefix
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output acaSubnetId string = vnet.properties.subnets[0].id
output acaSubnetName string = vnet.properties.subnets[0].name
output storageSubnetId string = vnet.properties.subnets[1].id
output storageSubnetName string = vnet.properties.subnets[1].name
