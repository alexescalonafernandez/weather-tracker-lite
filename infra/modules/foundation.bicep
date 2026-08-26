targetScope = 'resourceGroup'

param acrName string
param containerAppsEnvironmentName string
param identityName string
param location string
param logAnalyticsRetentionInDays int
param workspaceName string

module logAnalytics 'log-analytics.bicep' = {
  name: 'log-analytics'
  params: {
    location: location
    retentionInDays: logAnalyticsRetentionInDays
    workspaceName: workspaceName
  }
}

module containerAppsEnvironment 'container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    containerAppsEnvironmentName: containerAppsEnvironmentName
    location: location
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.primarySharedKey
  }
}

module acr 'acr.bicep' = {
  name: 'acr'
  params: {
    acrName: acrName
    location: location
  }
}

module managedIdentity 'managed-identity.bicep' = {
  name: 'managed-identity'
  params: {
    identityName: identityName
    location: location
  }
}

module acrPullRole 'acr-pull-role.bicep' = {
  name: 'acr-pull-role'
  params: {
    acrResourceId: acr.outputs.resourceId
    principalId: managedIdentity.outputs.principalId
  }
}

output acrLoginServer string = acr.outputs.loginServer
