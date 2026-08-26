targetScope = 'resourceGroup'

param budgetName string
param budgetNotificationEmail string
param budgetStartDate string

resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: budgetName
  properties: {
    amount: 10
    category: 'Cost'
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actualCostAt80Percent: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [
          budgetNotificationEmail
        ]
        contactGroups: []
        contactRoles: []
        locale: 'en-us'
      }
    }
  }
}
