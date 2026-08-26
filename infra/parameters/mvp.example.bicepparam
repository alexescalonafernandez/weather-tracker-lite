using '../main.bicep'

param namePrefix = 'weathertracker'
param environment = 'mvp'
param location = 'westeurope'
param logAnalyticsRetentionInDays = 30

// Replace this invalid placeholder through an approved deployment interface.
param budgetNotificationEmail = '<budget-recipient-required-at-deployment>'
