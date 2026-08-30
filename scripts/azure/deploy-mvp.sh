#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  AZURE_BUDGET_NOTIFICATION_EMAIL=<approved-address> scripts/azure/deploy-mvp.sh [options]

Default mode performs the non-mutating foundation preflight. Add --apply to create
the foundation, publish an immutable image, deploy the workload, and run smoke checks.

Required in every mode:
  --location <azure-region>
  --foundation-parameters <path>  Approved foundation .bicepparam file

Required with --apply:
  --workload-parameters <path>    Approved workload .bicepparam file
  --image-repository <name>       Repository path inside the derived ACR
  --foundation-deployment-name <name>
  --workload-deployment-name <name>
  --smoke-timeout-seconds <seconds>

Environment:
  AZURE_BUDGET_NOTIFICATION_EMAIL  Required approved budget notification address.
                                  Never provide it as a command-line argument or file.

Safety:
  The default mode does not create resources, push images, or deploy a workload.
  --apply requires a clean Git working tree and deploys only a digest-pinned image.
EOF
}

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"; }
require_value() { [[ -n "${2:-}" ]] || fail "$1 is required"; }

apply=false
location=''
foundation_parameters=''
workload_parameters=''
image_repository=''
foundation_deployment_name=''
workload_deployment_name=''
smoke_timeout_seconds=''

while (($#)); do
  case "$1" in
    --apply) apply=true; shift ;;
    --location|--foundation-parameters|--workload-parameters|--image-repository|--foundation-deployment-name|--workload-deployment-name|--smoke-timeout-seconds)
      (($# >= 2)) || fail "Missing value for $1"
      case "$1" in
        --location) location="$2" ;;
        --foundation-parameters) foundation_parameters="$2" ;;
        --workload-parameters) workload_parameters="$2" ;;
        --image-repository) image_repository="$2" ;;
        --foundation-deployment-name) foundation_deployment_name="$2" ;;
        --workload-deployment-name) workload_deployment_name="$2" ;;
        --smoke-timeout-seconds) smoke_timeout_seconds="$2" ;;
      esac
      shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) fail "Unknown option: $1" ;;
  esac
done

require_command az
require_command git
require_value '--location' "$location"
require_value '--foundation-parameters' "$foundation_parameters"
require_value 'AZURE_BUDGET_NOTIFICATION_EMAIL environment variable' "${AZURE_BUDGET_NOTIFICATION_EMAIL:-}"
[[ "$AZURE_BUDGET_NOTIFICATION_EMAIL" != *$'\n'* && "$AZURE_BUDGET_NOTIFICATION_EMAIL" != *$'\r'* ]] || fail 'AZURE_BUDGET_NOTIFICATION_EMAIL must be a single-line value'

repository_root="$(git rev-parse --show-toplevel)" || fail 'Run from a Git working tree'
cd "$repository_root"
[[ -f "$foundation_parameters" ]] || fail "Foundation parameter file not found: $foundation_parameters"
foundation_parameter_content="$(<"$foundation_parameters")"
[[ "$foundation_parameter_content" =~ param[[:space:]]+budgetNotificationEmail[[:space:]]*=[[:space:]]*readEnvironmentVariable\([[:space:]]*['\"]AZURE_BUDGET_NOTIFICATION_EMAIL['\"] ]] || fail 'Foundation parameter file must bind budgetNotificationEmail with readEnvironmentVariable(AZURE_BUDGET_NOTIFICATION_EMAIL)'

build_bicep() {
  local source_file="$1"
  local output_file
  output_file="$(mktemp)"
  rm -f "$output_file"
  az bicep build --file "$source_file" --outfile "$output_file"
  rm -f "$output_file"
}

build_parameters() {
  local parameter_file="$1"
  local output_file
  output_file="$(mktemp)"
  rm -f "$output_file"
  az bicep build-params --file "$parameter_file" --outfile "$output_file"
  rm -f "$output_file"
}

printf '%s\n' 'Running non-mutating Bicep compilation and foundation validation.'
build_bicep infra/main.bicep
build_bicep infra/workload.bicep
build_parameters "$foundation_parameters"
az deployment sub validate \
  --location "$location" \
  --template-file infra/main.bicep \
  --parameters "$foundation_parameters"
az deployment sub what-if \
  --location "$location" \
  --template-file infra/main.bicep \
  --parameters "$foundation_parameters"

if [[ "$apply" == false ]]; then
  printf '%s\n' 'Preflight completed. Re-run with --apply and all apply-only options after approval.'
  exit 0
fi

require_command curl
require_value '--workload-parameters' "$workload_parameters"
require_value '--image-repository' "$image_repository"
require_value '--foundation-deployment-name' "$foundation_deployment_name"
require_value '--workload-deployment-name' "$workload_deployment_name"
require_value '--smoke-timeout-seconds' "$smoke_timeout_seconds"
[[ -f "$workload_parameters" ]] || fail "Workload parameter file not found: $workload_parameters"
build_parameters "$workload_parameters"
[[ "$image_repository" =~ ^[a-z0-9]+([._/-][a-z0-9]+)*$ ]] || fail 'Image repository must be a lowercase Docker repository path'
[[ "$smoke_timeout_seconds" =~ ^[1-9][0-9]*$ ]] || fail '--smoke-timeout-seconds must be a positive integer'
git diff --quiet && git diff --cached --quiet || fail 'Refusing --apply with a dirty Git working tree; the image must match its Git SHA'

printf '%s\n' 'Creating the approved foundation deployment.'
az deployment sub create \
  --name "$foundation_deployment_name" \
  --location "$location" \
  --template-file infra/main.bicep \
  --parameters "$foundation_parameters"

resource_group_name="$(az deployment sub show --name "$foundation_deployment_name" --query 'properties.outputs.resourceGroupName.value' --output tsv)"
acr_name="$(az deployment sub show --name "$foundation_deployment_name" --query 'properties.outputs.acrName.value' --output tsv)"
acr_login_server="$(az deployment sub show --name "$foundation_deployment_name" --query 'properties.outputs.acrLoginServer.value' --output tsv)"
require_value 'Foundation output resourceGroupName' "$resource_group_name"
require_value 'Foundation output acrName' "$acr_name"
require_value 'Foundation output acrLoginServer' "$acr_login_server"
[[ "$resource_group_name" != *$'\n'* && "$acr_name" != *$'\n'* && "$acr_login_server" != *$'\n'* ]] || fail 'Foundation outputs must be single-line values'

git_sha="$(git rev-parse --verify HEAD)"
image_tag="$acr_login_server/$image_repository:$git_sha"
printf '%s\n' "Building and publishing source SHA image: $image_tag"
require_command docker
docker build --tag "$image_tag" .
az acr login --name "$acr_name"
docker push "$image_tag"
image_digest="$(az acr repository show --name "$acr_name" --image "$image_repository:$git_sha" --query digest --output tsv)"
[[ "$image_digest" =~ ^sha256:[a-f0-9]{64}$ ]] || fail 'ACR did not return a valid SHA-256 image digest'
image_reference="$acr_login_server/$image_repository@$image_digest"

printf '%s\n' "Validating, reviewing, and deploying digest-pinned workload image: $image_reference"
az deployment group validate \
  --resource-group "$resource_group_name" \
  --template-file infra/workload.bicep \
  --parameters "$workload_parameters" "imageReference=$image_reference"
az deployment group what-if \
  --resource-group "$resource_group_name" \
  --template-file infra/workload.bicep \
  --parameters "$workload_parameters" "imageReference=$image_reference"
az deployment group create \
  --name "$workload_deployment_name" \
  --resource-group "$resource_group_name" \
  --template-file infra/workload.bicep \
  --parameters "$workload_parameters" "imageReference=$image_reference"

container_app_name="$(az deployment group show --name "$workload_deployment_name" --resource-group "$resource_group_name" --query 'properties.outputs.containerAppName.value' --output tsv)"
container_app_fqdn="$(az deployment group show --name "$workload_deployment_name" --resource-group "$resource_group_name" --query 'properties.outputs.containerAppFqdn.value' --output tsv)"
require_value 'Workload output containerAppName' "$container_app_name"
require_value 'Workload output containerAppFqdn' "$container_app_fqdn"
deployed_image="$(az containerapp show --name "$container_app_name" --resource-group "$resource_group_name" --query 'properties.template.containers[0].image' --output tsv)"
[[ "$deployed_image" == "$image_reference" ]] || fail 'Active Container App template does not match the requested immutable image reference'

deadline=$(( $(date +%s) + smoke_timeout_seconds ))
smoke_paths=(/health/live /health/ready /)
while :; do
  smoke_passed=true
  for smoke_path in "${smoke_paths[@]}"; do
    if ! curl --fail --silent --show-error --max-time 10 --output /dev/null "https://$container_app_fqdn$smoke_path"; then
      smoke_passed=false
    fi
  done
  [[ "$smoke_passed" == true ]] && break
  (( $(date +%s) < deadline )) || fail "Smoke checks did not all pass before the ${smoke_timeout_seconds}-second deadline"
  sleep 10
done

printf '%s\n' "Deployment and bounded smoke checks completed for image digest $image_digest."
