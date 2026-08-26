targetScope = 'resourceGroup'

param acrResourceId string
param principalId string

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: last(split(acrResourceId, '/'))
}

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, principalId, 'AcrPull')
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

output resourceId string = acrPullRoleAssignment.id
