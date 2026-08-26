targetScope = 'resourceGroup'

@description('Prefix used to derive the foundation resource names.')
@minLength(3)
@maxLength(15)
param namePrefix string = 'weathertracker'

@description('Deployment environment label used to derive the foundation resource names.')
@minLength(2)
@maxLength(10)
param environment string = 'mvp'

@description('Azure region for the Container App.')
param location string = resourceGroup().location

@description('Previously published repository and immutable digest in the foundation ACR, for example weather-tracker-lite@sha256:<digest>.')
param imageReference string

var nameSeed = uniqueString(subscription().id, resourceGroup().name)
var acrName = toLower('${namePrefix}${environment}${nameSeed}')
var containerAppName = '${namePrefix}-${environment}-app'
var environmentName = '${namePrefix}-${environment}-cae-${take(nameSeed, 6)}'
var identityName = '${namePrefix}-${environment}-pull-${take(nameSeed, 6)}'

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: acrName
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' existing = {
  name: environmentName
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

module containerApp 'modules/container-app.bicep' = {
  name: 'container-app'
  params: {
    acrLoginServer: acr.properties.loginServer
    containerAppName: containerAppName
    containerAppsEnvironmentId: containerAppsEnvironment.id
    imageReference: imageReference
    location: location
    managedIdentityResourceId: managedIdentity.id
  }
}

output containerAppName string = containerAppName
output containerAppFqdn string = containerApp.outputs.fqdn
