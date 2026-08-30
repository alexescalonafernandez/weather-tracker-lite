# Azure MVP Lifecycle Scripts

These scripts provide a manual, auditable execution path for the Azure MVP. Bicep remains the source of truth for every resource and configuration decision.

## Quick path

1. Prepare approved, valid foundation and workload Bicep parameter files outside source control.
2. Export `AZURE_BUDGET_NOTIFICATION_EMAIL` in the operator shell; do not pass the address through arguments or files.
3. Run `deploy-mvp.sh` without `--apply` to compile, validate, and review the foundation what-if. Add `--apply` only after approval.
4. Run `teardown-mvp.sh --resource-group <exact-name>` to inventory resources. Deletion additionally requires `--apply`, `--confirm <exact-name>`, and a bounded timeout.

## Prerequisites

- Azure CLI authenticated to the intended subscription, with Bicep support and authorized for the required operations.
- Docker and Git for deployment; `curl` is additionally required for applied smoke checks.
- Approved parameter files, Azure region, deployment names, image repository, and explicit timeouts. The scripts intentionally do not choose these values. The foundation parameter file must bind `budgetNotificationEmail` with `readEnvironmentVariable('AZURE_BUDGET_NOTIFICATION_EMAIL')`; it must not contain the address.

## Deploy

```bash
export AZURE_BUDGET_NOTIFICATION_EMAIL='approved-operator@example.invalid'

# Non-mutating foundation compilation, validate, and what-if.
scripts/azure/deploy-mvp.sh \
  --location <azure-region> \
  --foundation-parameters <approved-foundation.bicepparam>

# Approved lifecycle execution.
scripts/azure/deploy-mvp.sh --apply \
  --location <azure-region> \
  --foundation-parameters <approved-foundation.bicepparam> \
  --workload-parameters <approved-workload.bicepparam> \
  --image-repository <repository> \
  --foundation-deployment-name <foundation-deployment-name> \
  --workload-deployment-name <workload-deployment-name> \
  --smoke-timeout-seconds <seconds>
```

Applied deployment creates the Bicep foundation, reads its resource group and registry outputs, builds and pushes an image tagged with the current clean Git SHA, resolves its SHA-256 digest, and deploys only the fully qualified digest reference. It never uses ACR admin credentials or a tag as deployment input.

## Teardown

```bash
# Read-only inventory.
scripts/azure/teardown-mvp.sh --resource-group <exact-resource-group-name>

# Explicit destructive operation.
scripts/azure/teardown-mvp.sh \
  --resource-group <exact-resource-group-name> \
  --apply \
  --confirm <exact-resource-group-name> \
  --timeout-seconds <seconds>
```

The teardown script only deletes the exact named resource group after confirmation, then polls until Azure reports it absent. It has no global, inferred, or blank deletion target.

## Safety model

The default deployment mode is non-mutating, and the default teardown mode is read-only. Applied deployment requires `--apply`, a clean Git tree, explicit operator inputs, and the budget notification environment variable. Applied teardown requires the exact resource-group name twice. Review [the Azure deployment runbook](../../docs/azure-deployment-runbook.md) for the authoritative operational procedure and evidence requirements.
