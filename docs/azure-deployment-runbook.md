# Azure MVP Deployment Runbook

## Purpose and operating rule

This runbook defines the **future** build, publish, deployment, and verification sequence for the approved Azure MVP. Commands are placeholders only: replace angle-bracket values in an approved operator environment, validate first, and do not run a deployment from this document without the Bicep implementation and access approval.

The deployment unit is one ASP.NET Core container listening on port `8080`. Its public endpoint is the generated Container Apps FQDN. Deployment is complete only after image, revision, probes, logs, and smoke checks pass.

For a guarded manual command implementation of this procedure, use [`scripts/azure/`](../scripts/azure/README.md). The scripts do not replace this runbook or the Bicep source of truth.

## Preflight requirements

| Requirement | Evidence required before mutation |
| --- | --- |
| Approved topology | Current approval for `westeurope`, Consumption Container Apps Environment, Basic ACR, one external Container App, Log Analytics, user-assigned identity, and USD 10 monthly alert budget. |
| Bicep implementation | Reviewed modules and non-secret parameter file matching the infrastructure design. |
| Azure access | An operator identity with least privilege to deploy the resource group resources and registry-scoped RBAC assignment. Do not record identity values in artifacts. |
| Tools | Current Azure CLI with Bicep support, Docker-compatible image builder, and access to the chosen registry. |
| Naming | Approved, unique resource names; no personal names, absolute paths, tenant IDs, or subscription IDs in committed files. |
| Image version | A planned immutable tag or digest tied to the source revision being deployed. |
| Notifications | An approved budget-alert notification destination supplied outside source control. |
| Availability | Bicep preflight evidence that the **exact** Azure resource types, API versions, region, Consumption environment capability, Basic ACR SKU, and requested features are available before deployment. |

## Non-mutating validation

Perform and record these checks before creating or changing Azure resources. They must not deploy, push an image, or delete anything.

```bash
# Confirm the intended Azure CLI account and selected subscription context.
az account show

# Confirm the required resource provider registrations and regional support.
az provider show --namespace Microsoft.App
az provider show --namespace Microsoft.ContainerRegistry
az provider show --namespace Microsoft.OperationalInsights
az provider show --namespace Microsoft.Consumption
az provider show --namespace Microsoft.ManagedIdentity

# Compile both stage entry points and their parameter files.
az bicep build --file infra/main.bicep
az bicep build --file infra/workload.bicep
az bicep build-params --file infra/parameters/mvp.example.bicepparam
az bicep build-params --file infra/parameters/workload.example.bicepparam

# After supplying the external values, validate and review each stage at its scope.
az deployment sub validate --location westeurope --template-file infra/main.bicep --parameters infra/parameters/mvp.example.bicepparam budgetNotificationEmail=<approved-recipient>
az deployment sub what-if --location westeurope --template-file infra/main.bicep --parameters infra/parameters/mvp.example.bicepparam budgetNotificationEmail=<approved-recipient>
az deployment group validate --resource-group <resource-group-name> --template-file infra/workload.bicep --parameters infra/parameters/workload.example.bicepparam imageReference=<repository>@sha256:<digest>
az deployment group what-if --resource-group <resource-group-name> --template-file infra/workload.bicep --parameters infra/parameters/workload.example.bicepparam imageReference=<repository>@sha256:<digest>

# Build the application image locally without publishing it.
docker build --tag <local-image-name>:<immutable-tag> .
```

`main.bicep` is the subscription-scope foundation entry point. `workload.bicep` is the resource-group-scope workload entry point. A successful compile or what-if does not replace checking provider registration, regional availability, SKU availability, and policy constraints.

Stop if validation reveals unavailable features, prohibited policy, unexpected resources, a floating image tag, any credential-based ACR design, or a discrepancy from the approved resource inventory.

## Future deployment sequence

Execute this sequence only after preflight approval. Values shown in angle brackets are placeholders.

1. **Create the dedicated resource group and supporting infrastructure.**

   ```bash
   az deployment sub create --location westeurope --template-file infra/main.bicep --parameters infra/parameters/mvp.example.bicepparam budgetNotificationEmail=<approved-recipient>
   ```

   This foundation stage creates the resource group, Log Analytics workspace, Consumption Container Apps Environment, Basic ACR, user-assigned managed identity, registry-scoped `AcrPull` assignment, and USD 10 budget. It deliberately does not create a Container App. Confirm the ACR admin user remains disabled.

2. **Build and publish an immutable image to the private registry.**

   ```bash
   docker build --tag <acr-login-server>/<repository>:<immutable-tag> .
   az acr login --name <acr-name>
   docker push <acr-login-server>/<repository>:<immutable-tag>
   az acr repository show --name <acr-name> --image <repository>:<immutable-tag>
   ```

   Use an approved operator authentication flow for the push. This operator authorization is distinct from the Container App runtime identity, which requires only `AcrPull`.

3. **Deploy or update the Container App revision.**

   ```bash
   az deployment group create --resource-group <resource-group-name> --template-file infra/workload.bicep --parameters infra/parameters/workload.example.bicepparam imageReference=<repository>@sha256:<digest>
   ```

   Obtain `<repository>@sha256:<digest>` from the published image before this stage; do not use a tag. The deployment must configure external ingress on port `8080`, attach the user-assigned identity as the ACR registry identity, set `minReplicas: 0` and `maxReplicas: 1`, and map startup/liveness to `/health/live` and readiness to `/health/ready`.

4. **Capture the generated public endpoint and deployed revision.**

   ```bash
   az containerapp show --name <container-app-name> --resource-group <resource-group-name> --query properties.configuration.ingress.fqdn --output tsv
   ```

   Record the FQDN, active revision, immutable image reference, deployment timestamp, and validation result in the deployment record. Do not copy secrets or operator identity details into that record.

## Smoke checks

Run after the revision becomes active. The first request can take longer when the app is scaled to zero.

```bash
# Liveness must return a successful HTTP response.
curl --fail --silent --show-error https://<container-app-fqdn>/health/live

# Readiness must return a successful HTTP response.
curl --fail --silent --show-error https://<container-app-fqdn>/health/ready

# Confirm the public application endpoint responds.
curl --fail --silent --show-error https://<container-app-fqdn>/
```

Validate a representative city lookup manually in the browser. Confirm that a successful result includes weather data and attribution, and that invalid or unavailable data results in a safe visitor-facing message rather than diagnostics.

If smoke checks fail, do not broaden access or disable probes to force traffic through. Inspect revision state and logs, verify port `8080`, then correct the deployment or application issue and deploy a new immutable image revision.

## Probe and log checks

| Check | Expected result | Investigation if it fails |
| --- | --- | --- |
| Startup probe `/health/live` | The revision starts and becomes active. | Confirm the container command, listening port `8080`, and startup timing. |
| Liveness probe `/health/live` | The process remains HTTP-responsive. | Inspect restart events and application stdout/stderr. Do not make it depend on Open-Meteo. |
| Readiness probe `/health/ready` | The revision accepts traffic after local configuration validates. | Inspect startup configuration logs and the Open-Meteo configuration health check. Provider availability is not a readiness dependency. |
| Container logs in Log Analytics | Startup, request, workflow, and provider outcome logs are queryable for the active revision. | Confirm the environment diagnostic destination and the workspace link. |
| Scale settings | At idle the app may reach zero; never more than one replica runs. | Inspect effective scale configuration and active revision settings. |

Placeholder operational queries:

```bash
# Inspect Container App and revision state.
az containerapp show --name <container-app-name> --resource-group <resource-group-name>
az containerapp revision list --name <container-app-name> --resource-group <resource-group-name>

# Retrieve recent Container Apps logs through the approved Log Analytics query path.
az monitor log-analytics query --workspace <workspace-id> --analytics-query <approved-query>
```

Keep log retention intentional and periodically review volume. Log Analytics is the MVP troubleshooting destination; Application Insights is deferred.

## Cost and budget controls

1. The foundation deployment creates a monthly **USD 10** budget for the dedicated MVP resource group.
2. Supply the approved alert destination outside source control as `budgetNotificationEmail`.
3. Verify the budget is visible in Azure Cost Management and record its scope and thresholds.
4. Review costs after the first deployment and regularly thereafter, including Log Analytics ingestion/retention and ACR storage.

Budget alerts warn; they do **not** stop, scale down, or delete resources. The operational response to an alert is to review usage and, if the environment is no longer needed, use the teardown procedure below.

## Teardown: delete the dedicated resource group

Teardown is explicit and destructive. It is the approved way to remove the MVP because all approved workload resources are contained in the dedicated resource group. Confirm no shared resources or required evidence depend on it first.

```bash
# Review the resource inventory, export any required non-secret operational evidence,
# then delete only the dedicated MVP resource group.
az resource list --resource-group <resource-group-name>
az group delete --name <resource-group-name> --yes

# Verify deletion has completed before considering the environment removed.
az group exists --name <resource-group-name>
```

Do not delete individual resources as a substitute for resource-group teardown: partial deletion leaves cost and RBAC drift. A later recreation must repeat preflight, use the approved Bicep deployment, publish an immutable image, and create a fresh deployment record.

## Completion checklist

- [ ] Non-mutating Bicep and availability preflight passed for the exact deployment configuration.
- [ ] Private ACR image was published with an immutable reference.
- [ ] User-assigned managed identity has registry-scoped `AcrPull`; no admin credential path is enabled.
- [ ] Container App uses external port `8080`, public default FQDN, and scale range zero through one.
- [ ] `/health/live` and `/health/ready` passed after the new revision became active.
- [ ] Logs are available in Log Analytics and probe/revision status was inspected.
- [ ] USD 10 monthly budget alert is configured and understood to be non-blocking.
- [ ] If the environment is no longer required, the dedicated resource group has been deleted and deletion verified.
