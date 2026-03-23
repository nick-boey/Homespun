targetScope = 'subscription'

@description('Name of the resource group to create')
@minLength(1)
@maxLength(90)
param resourceGroupName string

@description('Azure region for all resources')
param location string = 'australiaeast'

@description('VM size for the Homespun server')
@allowed([
  'Standard_D2s_v3'
  'Standard_D4s_v3'
  'Standard_D8s_v3'
  'Standard_D2s_v5'
  'Standard_D4s_v5'
  'Standard_D8s_v5'
])
param vmSize string = 'Standard_D4s_v3'

@description('Admin username for SSH access')
@minLength(1)
param adminUsername string = 'homespun'

@description('SSH public key for admin access')
@minLength(1)
param adminSshPublicKey string

@description('Domain name for the server (used for Let\'s Encrypt SSL)')
param domainName string = ''

@description('GitHub personal access token with repo scope')
@secure()
param githubToken string = ''

@description('Claude Code OAuth token')
@secure()
param claudeCodeOAuthToken string = ''

@description('Tailscale auth key for VPN access')
@secure()
param tailscaleAuthKey string = ''

@description('OS disk size in GB')
@minValue(30)
@maxValue(1024)
param osDiskSizeGb int = 64

@description('Base name prefix for all resources')
@minLength(1)
param baseName string = 'homespun'

var tags = {
  application: 'homespun'
  managedBy: 'bicep'
}

// Resource Group
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Networking
module network 'modules/network.bicep' = {
  name: 'network-deployment'
  scope: rg
  params: {
    location: location
    baseName: baseName
    tags: tags
  }
}

// Cloud-init custom data
var cloudInitConfig = loadTextContent('cloud-init.yaml')
var cloudInitWithSecrets = replace(
  replace(
    replace(
      replace(cloudInitConfig, '__GITHUB_TOKEN__', githubToken),
      '__CLAUDE_CODE_OAUTH_TOKEN__', claudeCodeOAuthToken),
    '__TAILSCALE_AUTH_KEY__', tailscaleAuthKey),
  '__DOMAIN_NAME__', domainName)

// Virtual Machine
module vm 'modules/vm.bicep' = {
  name: 'vm-deployment'
  scope: rg
  params: {
    location: location
    baseName: baseName
    vmSize: vmSize
    nicId: network.outputs.nicId
    adminUsername: adminUsername
    adminSshPublicKey: adminSshPublicKey
    customData: base64(cloudInitWithSecrets)
    osDiskSizeGb: osDiskSizeGb
    tags: tags
  }
}

output resourceGroupName string = rg.name
output vmName string = vm.outputs.vmName
output publicIpAddress string = network.outputs.publicIpAddress
output adminUsername string = adminUsername
output sshCommand string = 'ssh ${adminUsername}@${network.outputs.publicIpAddress}'
