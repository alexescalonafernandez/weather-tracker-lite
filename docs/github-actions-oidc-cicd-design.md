# GitHub Actions OIDC CI/CD Design

## Approved outcome

GitHub Actions verifies every pull request without Azure access. A protected-main publisher builds and publishes a source-SHA-tagged container image, records its registry digest, and does not deploy. A separately protected deployment environment deploys only `infra/workload.bicep` with that immutable digest. Foundation creation and teardown remain manual operator procedures.

This separation keeps untrusted pull-request execution outside Azure, separates registry publishing from resource deployment, and makes each deployed revision traceable to both a source SHA and an image digest.

## Delivery path

1. **Pull request verification:** restore, build, and test the .NET application; build the Docker image; compile Bicep. This workflow has no Azure authentication and does not push or deploy.
2. **Protected-main publication:** after protected main is updated, build the image, tag it with the full source SHA, authenticate to Azure through OIDC, push to the existing ACR, resolve the pushed digest, and save the digest as a workflow artifact and job output.
3. **Protected deployment:** an authorized deployment invocation selects a recorded publisher digest and deploys `infra/workload.bicep` only. It passes `imageReference` as `<repository>@sha256:<digest>`; it never deploys a tag.
4. **Post-deployment validation:** read back the active revision image and health endpoints. Record the source SHA, digest, deployment result, and rollback decision without recording identities, account identifiers, credentials, or generated endpoints.

## Workflow boundaries

| Workflow or job | Trigger and protection | Azure access | Permitted work | Prohibited work |
| --- | --- | --- | --- | --- |
| PR verification | Pull request | None | Restore, build, test, Docker build, Bicep compilation | Azure login, image push, deployment, resource mutation |
| Publisher | Protected `main` only | Publisher identity | Build, SHA-tag, push to existing ACR, resolve and record digest | Bicep deployment, RBAC changes, foundation, teardown |
| Deployment | Separately protected deployment environment | Deployer identity | Deploy `infra/workload.bicep` using a recorded digest; validate the revision | Foundation deployment, teardown, image push, tag-based rollout |

The Docker build validates the existing multi-stage image that publishes the ASP.NET Core application and exposes port `8080`. Bicep compilation covers both entry points during PR verification, while the deployment workflow is deliberately limited to the resource-group-scoped workload entry point.

## Identity, permissions, and token policy

Each Azure login uses GitHub OIDC. Job permissions are limited to:

```yaml
permissions:
  contents: read
  id-token: write
```

`id-token: write` is needed only in publisher and deployment jobs that request an OIDC token. PR verification does not need it because it does not authenticate to Azure. No client secret is created, stored, or used.

| Identity | Federated subject restriction | Azure role and scope | Purpose |
| --- | --- | --- | --- |
| Publisher federated identity | Exact protected-main subject: `repo:<repository>:ref:refs/heads/main` | `AcrPush` on the existing ACR only | Push the SHA-tagged image and obtain its digest |
| Deployer federated identity | Exact environment subject: `repo:<repository>:environment:<deployment-environment>` | `Contributor` on the dedicated workload resource group initially | Deploy or update the workload from a digest |
| Runtime user-assigned managed identity | Not a GitHub federated identity | `AcrPull` on the ACR only | Pull the selected private image when the Container App revision runs |

`<repository>` must resolve to the single approved repository identity, and `<deployment-environment>` must be the exact protected GitHub Environment name. Do not add wildcard subjects, branch-pattern subjects, pull-request subjects, reusable-workflow subjects, or subjects for forks. The publisher identity must not be accepted by the deployment environment, and the deployer identity must not have registry push permission.

The initial resource-group `Contributor` grant for the deployer is an explicit starting point, not a permanent least-privilege claim. Reduce it after deployment activity identifies the exact required operations. The runtime identity remains `AcrPull` only and is never reused by either workflow.

## Required GitHub configuration

### Secrets

Configure these secrets at the narrowest appropriate GitHub scope. Their values are not documented here.

| Secret name | Used by |
| --- | --- |
| `AZURE_PUBLISHER_CLIENT_ID` | Publisher |
| `AZURE_DEPLOYER_CLIENT_ID` | Deployment |
| `AZURE_TENANT_ID` | Publisher and deployment |
| `AZURE_SUBSCRIPTION_ID` | Publisher and deployment |

No client-secret secret is required. Repository variables may hold non-sensitive, reviewable deployment inputs such as the resource-group name, image repository, ACR login server, Container App name, and deployment-environment label, once their governance scope is decided.

### Repository variables

Configure these non-secret variables at repository scope.

| Variable name | Used by | Purpose |
| --- | --- | --- |
| `AZURE_ACR_NAME` | Publisher | Existing ACR name for Docker login and digest lookup. |
| `AZURE_ACR_LOGIN_SERVER` | Publisher | Existing ACR login server for the SHA-tagged image reference. |
| `AZURE_RESOURCE_GROUP` | Deployment | Existing resource group containing the workload. |
| `AZURE_CONTAINER_APP_NAME` | Deployment | Existing Container App whose active revision is verified. |

The Publisher reads these values directly rather than enumerating ACRs in the MVP resource group. This preserves its ACR-scoped `AcrPush` boundary: it can publish to and inspect the configured registry without requiring resource-group discovery permission.

### Environments, approvals, and concurrency

| Control | Required rule |
| --- | --- |
| Protected main | Only changes admitted through the protected `main` branch can invoke publication. |
| Deployment environment | Use a dedicated protected GitHub Environment with required reviewers; only that environment may run the deployment job. |
| Environment secrets | Scope deployment-sensitive secrets to the protected deployment environment. Publisher secrets must not be exposed to pull-request jobs. |
| Concurrency | Use one deployment concurrency group per target environment with `cancel-in-progress: false`. The active rollout is never cancelled, but GitHub retains at most one pending deployment; a newer invocation replaces an earlier pending one. Operators must re-dispatch a superseded digest after the active rollout completes. |
| Provenance | Deployment input must be a publisher-recorded digest and its associated source SHA, not a manually typed tag. |

## Immutable image contract

The publisher tags the image with the full Git commit SHA and pushes it to the existing ACR. After the push succeeds, it resolves the registry content digest and emits these immutable deployment facts:

| Fact | Required use |
| --- | --- |
| Source SHA | Traceability from the protected-main commit to the published image |
| SHA image tag | Human-readable registry lookup only; never deployment input |
| Image digest | Required deployment input and revision evidence |
| Fully qualified digest reference | Passed to `workload.bicep` as `imageReference` |

`infra/workload.bicep` already accepts `imageReference` and wires it to the Container App module. The deployment job must reject a value that is not a digest reference before Azure authentication or deployment.

## Deployment and rollback

The deployment job deploys only `infra/workload.bicep` at the existing dedicated resource-group scope. It must not invoke `infra/main.bicep`, because that entry point creates the foundation resource group and supporting services.

After deployment, verify that the active revision uses the requested digest and that `/health/live`, `/health/ready`, and the application root succeed. To accommodate scale-to-zero, all three checks share one bounded five-minute cold-start deadline: each request runs for at most 10 seconds and failed requests retry after 10 seconds until the shared deadline expires. The existing runbook notes that the first request can take longer when scale-to-zero activates the application.

Rollback is a new protected deployment of the last known-good, previously recorded digest. Do not retag an image, deploy a floating tag, alter the running image manually, disable probes, or broaden permissions to recover. If the revision cannot be made healthy, stop the rollout, preserve non-sensitive evidence, and use the approved manual incident and teardown boundaries where applicable.

## Setup and validation order

1. Confirm the manual foundation is present and its ACR, Container Apps Environment, user-assigned runtime identity, and registry-scoped `AcrPull` assignment match the Azure design.
2. Define the protected `main` branch and the dedicated protected deployment environment, including reviewers and deployment concurrency.
3. Create the two separate Azure application identities and only their target-restricted federated credentials.
4. Grant `AcrPush` to the publisher on the existing ACR and initial resource-group `Contributor` to the deployer. Confirm the runtime identity remains `AcrPull` only.
5. Configure the named GitHub secrets and the repository variables `AZURE_ACR_NAME`, `AZURE_ACR_LOGIN_SERVER`, `AZURE_RESOURCE_GROUP`, and `AZURE_CONTAINER_APP_NAME` in their approved scopes, without adding a client secret.
6. Implement and validate PR verification: restore, build, test, Docker build, and compilation of `infra/main.bicep` and `infra/workload.bicep`; prove that no Azure login occurs.
7. Validate protected-main publication with a non-production commit: verify the full-SHA tag, resolved digest, and recorded provenance.
8. Validate protected deployment with that exact digest: verify the workload-only deployment, deployed revision image, probes, application response, and deployment evidence.
9. Test rollback by redeploying a previously recorded known-good digest through the same protected deployment path.

## Non-goals

- Automating foundation deployment through `infra/main.bicep`.
- Automating resource-group teardown or deletion of individual Azure resources.
- Azure authentication, image publishing, or deployment from pull-request verification.
- Client secrets, ACR admin credentials, registry passwords, or credentials in source control.
- Reusing publisher, deployer, or runtime identities.
- Deploying an image by a mutable or floating tag.
- Expanding runtime access beyond registry-scoped `AcrPull`.
- Adding application telemetry, DNS, custom domains, secret-management services, or unrelated infrastructure changes.

## Decisions still needed

| Decision | Why it is needed before implementation |
| --- | --- |
| Deployment-environment name and reviewer policy | Determines the deployer federated subject and approval boundary. |
| Exact scope for the named secrets and non-sensitive variables | Ensures publisher and deployment credentials are not exposed more broadly than required. |
| Digest handoff retention and evidence location | Determines how long a deployment can select and prove a publisher-recorded digest. |
| Deployer role reduction plan | Converts the initial resource-group `Contributor` role into evidence-based least privilege. |
| Rollback authority and known-good digest selection rule | Prevents an emergency rollback from bypassing approvals or provenance. |
| Required post-deployment smoke-check owner and timeout policy | Makes deployment completion and rollback decisions repeatable. |

## Review checklist

- [ ] PR verification has no Azure authentication and performs restore, build, test, Docker build, and Bicep compilation.
- [ ] Publisher is restricted to the exact protected-main federated subject and has `AcrPush` only on the existing ACR.
- [ ] Deployment is restricted to the exact protected-environment federated subject and deploys only `infra/workload.bicep`.
- [ ] Every deployment uses a recorded `<repository>@sha256:<digest>` reference.
- [ ] Publisher, deployer, and runtime identities are distinct; the runtime identity has `AcrPull` only.
- [ ] No client secret, registry password, tenant identifier, subscription identifier, personal identity, or generated endpoint appears in source-controlled evidence.
- [ ] Foundation and teardown remain manual, approved operations.
