#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/azure/teardown-mvp.sh --resource-group <exact-name> [--apply --confirm <exact-name> --timeout-seconds <seconds>]

Default mode is read-only: it confirms whether the exact resource group exists and
lists its resources. Deletion requires --apply and a --confirm value exactly equal
to --resource-group. The script never supplies a default resource group.
EOF
}

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"; }
require_value() { [[ -n "${2:-}" ]] || fail "$1 is required"; }

resource_group=''
confirmation=''
timeout_seconds=''
apply=false

while (($#)); do
  case "$1" in
    --resource-group|--confirm|--timeout-seconds)
      (($# >= 2)) || fail "Missing value for $1"
      case "$1" in
        --resource-group) resource_group="$2" ;;
        --confirm) confirmation="$2" ;;
        --timeout-seconds) timeout_seconds="$2" ;;
      esac
      shift 2 ;;
    --apply) apply=true; shift ;;
    --help|-h) usage; exit 0 ;;
    *) fail "Unknown option: $1" ;;
  esac
done

require_command az
require_value '--resource-group' "$resource_group"
[[ "$resource_group" != *$'\n'* && "$resource_group" != *$'\r'* ]] || fail 'Resource group must be a single-line exact name'

exists="$(az group exists --name "$resource_group")"
if [[ "$exists" != true ]]; then
  printf '%s\n' "Resource group does not exist: $resource_group"
  exit 0
fi

printf '%s\n' "Read-only inventory for resource group: $resource_group"
az resource list --resource-group "$resource_group" --output table

if [[ "$apply" == false ]]; then
  printf '%s\n' 'Inventory completed. Deletion was not requested.'
  exit 0
fi

require_value '--confirm' "$confirmation"
require_value '--timeout-seconds' "$timeout_seconds"
[[ "$confirmation" == "$resource_group" ]] || fail '--confirm must exactly match --resource-group'
[[ "$timeout_seconds" =~ ^[1-9][0-9]*$ ]] || fail '--timeout-seconds must be a positive integer'

printf '%s\n' "Deleting resource group: $resource_group"
az group delete --name "$resource_group" --yes --no-wait

deadline=$(( $(date +%s) + timeout_seconds ))
while [[ "$(az group exists --name "$resource_group")" == true ]]; do
  (( $(date +%s) < deadline )) || fail "Deletion did not complete before the ${timeout_seconds}-second deadline"
  sleep 10
done

printf '%s\n' "Deletion verified: resource group no longer exists: $resource_group"
