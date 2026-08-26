using '../workload.bicep'

param namePrefix = 'weathertracker'
param environment = 'mvp'
param location = 'westeurope'

// Replace this invalid placeholder through an approved deployment interface.
param imageReference = '<repository@sha256:digest-required-at-deployment>'
