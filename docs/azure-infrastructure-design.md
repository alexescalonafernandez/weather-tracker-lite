# Azure MVP Infrastructure Design

## Decision record

**Approved outcome:** deploy the single Weather Tracker Lite container to Azure Container Apps in `westeurope`. The application is public through the platform-provided Container Apps FQDN and uses a private Azure Container Registry (ACR) image. This is the smallest topology that preserves reproducibility, private image distribution, health-aware operations, and an explicit destruction boundary.

| Decision | Approved MVP choice | Rationale |
| --- | --- | --- |
| Region | `westeurope` | Fixed deployment location for a small, auditable MVP. |
| Compute | Consumption Azure Container Apps Environment | Runs one HTTP workload without Kubernetes, VM, or VNet administration. |
| Application | One external Container App on port `8080` | Matches the container's exposed HTTP port and the one-deployable-unit design. |
| Public ingress | Default Container Apps FQDN | Enables the MVP without DNS or certificate management. |
| Image registry | Basic private ACR | Keeps the application image private with the lowest approved registry tier. |
| Image identity | User-assigned managed identity with `AcrPull` on the registry | Avoids registry credentials and makes image-pull permission explicit and auditable. |
| Logs | Log Analytics workspace | Provides the Container Apps Environment log destination without adding application telemetry services. |
| Scale | Minimum `0`, maximum `1` replica | Allows idle scale-to-zero and prevents horizontal scaling costs in the MVP. |
| Budget | USD 10 monthly alert budget | Creates an early cost signal; a budget alert does not stop or delete resources. |

## Approved topology

```text
Internet
  -> public default Container Apps FQDN
  -> Container App (external HTTP ingress :8080; 0..1 replicas)
       -> private Basic ACR (image pull through user-assigned identity + AcrPull)
       -> Container Apps Environment (Consumption)
            -> Log Analytics workspace (platform and container logs)

Resource group
  -> all MVP resources above
  -> monthly USD 10 budget and alert configuration
```

The application remains stateless. It calls the public Open-Meteo APIs at request time; no weather data is persisted in Azure.

## Resource inventory and justification

| Resource | Scope | Required configuration | Justification |
| --- | --- | --- | --- |
| Resource group | One MVP lifecycle boundary | Region `westeurope`; dedicated to this workload | Makes ownership, inventory, budget scope, and teardown unambiguous. |
| Log Analytics workspace | Resource group | Retention set deliberately to the lowest acceptable operational period | Required log destination for the Container Apps Environment; supports deployment and probe troubleshooting. |
| Container Apps Environment | Resource group | Consumption workload profile; sends logs to the workspace | Hosts the application without cluster management and permits scale-to-zero. |
| Azure Container Registry | Resource group | Basic SKU; public network access only as required for deployment and platform image pulls; admin user disabled | Stores the private container image at the approved minimum tier. |
| User-assigned managed identity | Resource group | Attached to the Container App | Gives the workload a stable Azure identity independent of a revision. |
| Role assignment | Registry scope | `AcrPull` assigned to the user-assigned identity | Grants only image-pull access; no registry administration or push permission. |
| Container App | Resource group | External ingress, target port `8080`, default FQDN, image from ACR, identity attached, `minReplicas: 0`, `maxReplicas: 1` | Runs the one ASP.NET Core container with public HTTP access and bounded scale. |
| Budget and alert | Subscription billing scope, constrained to the resource group where supported | USD 10 monthly cost budget; action group/notification recipient supplied as deployment-time input | Detects unexpected spend without attempting unsafe automatic shutdown. |

## Boundaries and non-goals

This design intentionally does **not** provision:

- Key Vault or application secrets. The current Open-Meteo endpoints require no credential; secrets must not be placed in Bicep parameters, images, or documentation.
- Application Insights. Container logs go to Log Analytics; application-level telemetry is a later, evidence-driven decision.
- Custom domain, managed certificate, or DNS zone. The default public FQDN is the MVP endpoint.
- Virtual network, private endpoints, firewall topology, or egress controls. These require a concrete networking requirement.
- Database, storage account, cache, queues, scheduled jobs, or eventing. City data is ephemeral.
- CI/CD workflows. Build and deployment commands are intentionally future operational steps, not automation delivered by this design.
- User-facing authentication or authorization.

The budget is an alerting control, not a circuit breaker. Cost containment comes from scale-to-zero, a one-replica ceiling, Basic ACR, intentional log retention, inventory review, and dedicated resource-group teardown.

## Identity and RBAC model

| Principal | Resource scope | Role | Allowed purpose | Explicitly not allowed |
| --- | --- | --- | --- | --- |
| User-assigned managed identity attached to the Container App | ACR resource | `AcrPull` | Pull the selected private image when a revision starts | Push/delete images, manage registry settings, or access unrelated resources. |
| Deployment operator identity | Determined outside this design | Least privilege required to create the approved resources and role assignment | Run an approved deployment after preflight | Embedded credentials or standing broad ownership granted by this design. |

The identity is created before the Container App and role assignment. The registry role assignment must use the identity's principal ID and the ACR resource ID. Image deployment must reference the user-assigned identity as the registry identity. No ACR admin account, username, password, access token, tenant value, subscription value, or user identity belongs in source control or parameters.

## Ingress, probes, and scaling

### Ingress

The Container App is external with HTTP traffic routed to target port `8080`. The Dockerfile exposes `8080` and configures ASP.NET Core to listen on that port. The public endpoint is the generated Container Apps FQDN; no custom host name is assumed.

### Health probes

The deployed Container App must map the existing endpoints as follows:

| Container Apps probe | Path | Port | Why |
| --- | --- | --- | --- |
| Startup | `/health/live` | `8080` | Allows sufficient startup time while checking that the process is serving HTTP. |
| Liveness | `/health/live` | `8080` | Detects a non-responsive process without depending on Open-Meteo. |
| Readiness | `/health/ready` | `8080` | Admits traffic only after application configuration health is ready. |

`/health/live` is intentionally lightweight. `/health/ready` currently checks the tagged Open-Meteo configuration health check, not Open-Meteo availability, so an upstream weather-provider outage must not make the container unready. Probe timing, thresholds, and initial delays are deployment parameters with conservative defaults validated against the actual Container Apps resource schema before deployment.

### Scaling

The app sets `minReplicas` to `0` and `maxReplicas` to `1`. HTTP ingress enables activation from zero for incoming requests. A single replica is appropriate only while the MVP's availability and throughput expectations remain modest; a future increase requires explicit cost and availability review.

## Bicep organization and parameters

Bicep is a future implementation, not an artifact created by this design. Its module boundary must mirror the resource inventory so a reviewer can trace every deployed resource to one decision.

```text
infra/
  main.bicep                         # orchestration, resource group scope, module wiring
  modules/
    log-analytics.bicep              # workspace and retention
    container-apps-environment.bicep # Consumption environment and log destination
    acr.bicep                         # Basic registry with admin user disabled
    managed-identity.bicep            # user-assigned identity
    acr-pull-role.bicep               # registry-scoped AcrPull assignment
    container-app.bicep               # ingress, probes, scale, identity, image reference
    budget.bicep                      # monthly USD 10 budget and alert target
  parameters/
    mvp.bicepparam                   # non-secret MVP values only
```

| Parameter group | Examples | Rule |
| --- | --- | --- |
| Naming and location | resource prefix, environment label, `westeurope` | Names must be deterministic and globally unique where Azure requires it. |
| Image | ACR name, repository, immutable image tag or digest | Never use a floating image reference for an auditable deployment. |
| Runtime | target port `8080`, minimum `0`, maximum `1`, CPU/memory choice | Keep bounded to the approved MVP profile. |
| Probes | live/ready paths and timing thresholds | Must target the existing endpoints and port. |
| Logs and cost | workspace retention, monthly budget `10`, alert notification target | Notification targets are deployment inputs, not committed values. |

Parameters must exclude secrets, registry credentials, tenant IDs, subscription IDs, absolute filesystem paths, and personal identities. Before any deployment, Bicep preflight must validate the exact regional resource types, API versions, SKUs, and feature availability that the selected modules require; design approval is not proof of availability.

## Review checklist

- [ ] Every resource is in the inventory and has a stated purpose.
- [ ] ACR is Basic, private, and has no admin credential path.
- [ ] The Container App uses the user-assigned identity and registry-scoped `AcrPull`.
- [ ] Public traffic uses the default FQDN on target port `8080`.
- [ ] Startup/liveness use `/health/live`; readiness uses `/health/ready`.
- [ ] Scaling is bounded to zero through one replica.
- [ ] Log retention and budget notifications have an owner-defined value before deployment.
- [ ] Exact resource availability is validated by Bicep preflight before deployment.
