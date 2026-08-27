# Azure Deployment Evidence

This portfolio record captures the completed Azure MVP deployment without retaining personal identities, account identifiers, credentials, absolute paths, or ephemeral generated host names. Generated values are intentionally retrieved at review time.

## Executive record

| Area | Completed evidence |
| --- | --- |
| Preflight | Required provider registrations were checked. The foundation and workload Bicep entry points passed validation and what-if review before mutation. |
| Foundation | The dedicated MVP resource group, Log Analytics workspace, Consumption Container Apps Environment, Basic private ACR, user-assigned managed identity, registry-scoped `AcrPull` assignment, and USD 10 monthly budget alert were deployed. |
| Image | The application image was published to ACR under an immutable tag, then identified by its content digest. |
| Workload | The Container App was deployed from the digest-pinned image through the managed identity; external ingress targets port `8080` and the replica range is zero through one. |
| Verification | The live and ready health endpoints, application root, and an Antwerp weather lookup each returned HTTP 200. |

## Application acceptance evidence

The deployed Razor Pages form was exercised through the normal token-aware antiforgery flow: retrieve the form to obtain its request-verification token and associated cookie, then submit Antwerp with both values. The successful response rendered exactly three forecast days and the Open-Meteo attribution.

| Check | Observed result |
| --- | --- |
| `GET /health/live` | HTTP 200 |
| `GET /health/ready` | HTTP 200 |
| `GET /` | HTTP 200 |
| Token-aware Antwerp form submission | HTTP 200 with weather result |
| Forecast cardinality | Exactly three days |
| Attribution | Open-Meteo attribution displayed |

The three-day constraint is enforced by the application and the provider request asks Open-Meteo for `forecast_days=3`; malformed or non-three-day responses are rejected rather than rendered.

## Reproducible readback

Use approved credentials and replace only the placeholders. These commands are read-only and keep generated values out of this record.

```bash
# Active public endpoint. The generated FQDN changes after teardown and recreation.
az containerapp show \
  --name <container-app-name> \
  --resource-group <resource-group-name> \
  --query properties.configuration.ingress.fqdn \
  --output tsv

# Latest deployed revision name.
az containerapp show \
  --name <container-app-name> \
  --resource-group <resource-group-name> \
  --query properties.latestRevisionName \
  --output tsv

# Digest of the immutable image tag that was published.
az acr repository show \
  --name <acr-name> \
  --image <repository>:<immutable-tag> \
  --query digest \
  --output tsv
```

Use the digest output to form the workload input `<repository>@sha256:<digest>`. Confirm the running revision's image value before any subsequent rollout:

```bash
az containerapp revision show \
  --name <revision-name> \
  --app <container-app-name> \
  --resource-group <resource-group-name> \
  --query 'properties.template.containers[].image' \
  --output tsv
```

## Boundaries and follow-up

- This is a temporary MVP deployment and its generated public FQDN is not a durable endpoint.
- The USD 10 monthly budget is an alerting threshold only; it does not stop, scale down, or delete resources.
- Application Insights, a custom domain, and CI/CD are intentionally not deployed yet.
- Log Analytics is the current troubleshooting destination; no application telemetry service was added.
- Teardown remains the dedicated resource-group deletion procedure described in the deployment runbook.

## Reviewer checklist

- [x] Non-mutating provider, validate, and what-if preflight completed before deployment.
- [x] Foundation resources and immutable ACR image publication completed.
- [x] Digest-pinned Container App revision and HTTP acceptance checks completed.
- [x] No generated FQDN, credential, personal identity, tenant/subscription identifier, or absolute path is retained here.
