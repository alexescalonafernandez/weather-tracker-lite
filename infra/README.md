# Azure MVP Bicep

Infrastructure has two non-overlapping deployment stages. Deploy them in order: foundation, image publication, then workload.

| Stage | Entry point and scope | Creates or updates | Parameter example |
| --- | --- | --- | --- |
| Foundation | `main.bicep` at subscription scope | Dedicated resource group, Log Analytics, Container Apps environment, Basic non-admin ACR, user-assigned identity, registry-scoped `AcrPull`, and budget | `parameters/mvp.example.bicepparam` |
| Workload | `workload.bicep` at resource-group scope | Container App only | `parameters/workload.example.bicepparam` |

## Deployment order

1. Supply `budgetNotificationEmail` outside source control and deploy the subscription-scope foundation using `parameters/mvp.example.bicepparam`.
2. Publish the application to the created private ACR with an immutable digest.
3. Supply that `imageReference` as `<repository>@sha256:<digest>` outside source control and deploy the resource-group-scope workload using `parameters/workload.example.bicepparam`.

The example files contain invalid placeholders for the externally supplied budget recipient and image digest. They must be replaced through an approved deployment interface. Neither entry point accepts application or registry secrets.

## Local validation

```bash
az bicep build --file infra/main.bicep --outfile /tmp/weather-tracker-lite-foundation.json
az bicep build --file infra/workload.bicep --outfile /tmp/weather-tracker-lite-workload.json
az bicep build-params --file infra/parameters/mvp.example.bicepparam --outfile /tmp/weather-tracker-lite-foundation.parameters.json
az bicep build-params --file infra/parameters/workload.example.bicepparam --outfile /tmp/weather-tracker-lite-workload.parameters.json
```

Use subscription-scope `az deployment sub validate` and `what-if` for foundation, and resource-group-scope `az deployment group validate` and `what-if` for workload, only after an operator supplies the required inputs and has the intended context. Neither command is part of this repository's local validation because the example files intentionally omit those values.
