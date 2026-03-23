@description('Azure region for the VM')
param location string

@description('Base name used as prefix for VM resources')
param baseName string

@description('VM size')
@allowed([
  'Standard_D2s_v3'
  'Standard_D4s_v3'
  'Standard_D8s_v3'
  'Standard_D2s_v5'
  'Standard_D4s_v5'
  'Standard_D8s_v5'
])
param vmSize string = 'Standard_D4s_v3'

@description('Network interface resource ID')
param nicId string

@description('Admin username for the VM')
@minLength(1)
param adminUsername string

@description('SSH public key for admin access')
@minLength(1)
param adminSshPublicKey string

@description('Cloud-init custom data (base64 encoded)')
param customData string = ''

@description('OS disk size in GB')
@minValue(30)
@maxValue(1024)
param osDiskSizeGb int = 64

@description('Tags to apply to all VM resources')
param tags object = {}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: '${baseName}-vm'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: baseName
      adminUsername: adminUsername
      customData: !empty(customData) ? customData : null
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: adminSshPublicKey
            }
          ]
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: '${baseName}-osdisk'
        createOption: 'FromImage'
        diskSizeGB: osDiskSizeGb
        managedDisk: {
          storageAccountType: 'Premium_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nicId
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

output vmId string = vm.id
output vmName string = vm.name
output principalId string = vm.identity.principalId
