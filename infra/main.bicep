targetScope = 'subscription'

@description('Prefix used to derive deterministic resource names.')
@minLength(3)
@maxLength(15)
param namePrefix string = 'weathertracker'

@description('Deployment environment label used to derive deterministic resource names.')
@minLength(2)
@maxLength(10)
param environment string = 'mvp'

@description('Azure region for the MVP resources.')
param location string = 'westeurope'

@description('Email address that receives the required budget notification. Supply at deployment time; do not commit it.')
param budgetNotificationEmail string

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param logAnalyticsRetentionInDays int = 30

@description('First day of the monthly budget period in UTC. Defaults to the month of deployment.')
param budgetStartDate string = '${utcNow('yyyy-MM-01')}T00:00:00Z'

var resourceGroupName = '${namePrefix}-${environment}-rg'
var mvpResourceGroupScope = resourceGroup(resourceGroupName)
var nameSeed = uniqueString(subscription().id, resourceGroupName)
var acrName = toLower('${namePrefix}${environment}${nameSeed}')
var workspaceName = '${namePrefix}-${environment}-logs-${take(nameSeed, 6)}'
var environmentName = '${namePrefix}-${environment}-cae-${take(nameSeed, 6)}'
var identityName = '${namePrefix}-${environment}-pull-${take(nameSeed, 6)}'
var budgetName = '${namePrefix}-${environment}-budget'

resource mvpResourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module foundation 'modules/foundation.bicep' = {
  name: 'foundation'
  scope: mvpResourceGroupScope
  dependsOn: [
    mvpResourceGroup
  ]
  params: {
    acrName: acrName
    containerAppsEnvironmentName: environmentName
    identityName: identityName
    location: location
    logAnalyticsRetentionInDays: logAnalyticsRetentionInDays
    workspaceName: workspaceName
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  scope: mvpResourceGroupScope
  dependsOn: [
    mvpResourceGroup
  ]
  params: {
    budgetName: budgetName
    budgetNotificationEmail: budgetNotificationEmail
    budgetStartDate: budgetStartDate
  }
}

output resourceGroupName string = resourceGroupName
output acrName string = acrName
output acrLoginServer string = foundation.outputs.acrLoginServer
