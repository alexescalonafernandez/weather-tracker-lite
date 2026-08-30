#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
teardown_script="$script_dir/teardown-mvp.sh"
mock_bin="$(mktemp -d)"
mock_state="$(mktemp -d)"
trap 'rm -rf "$mock_bin" "$mock_state"' EXIT

cat >"$mock_bin/az" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$MOCK_STATE_DIR/az-calls"
case "$1 $2" in
  'group exists')
    if [[ "${MOCK_SCENARIO:-normal}" == pending ]]; then
      printf 'true\n'
    else
      printf '\357\273\277TrUe\r\n'
    fi ;;
  'group show') printf 'Deleting\n' ;;
  'group delete') ;;
  'resource list') printf 'mock inventory\n' ;;
  *) exit 1 ;;
esac
EOF
chmod +x "$mock_bin/az"

cat >"$mock_bin/date" <<'EOF'
#!/usr/bin/env bash
count_file="$MOCK_STATE_DIR/date-count"
count=0
[[ -f "$count_file" ]] && count="$(<"$count_file")"
printf '%s' $((count + 1)) >"$count_file"
if ((count == 0)); then
  printf '100\n'
else
  printf '101\n'
fi
EOF
chmod +x "$mock_bin/date"

cat >"$mock_bin/sleep" <<'EOF'
#!/usr/bin/env bash
:
EOF
chmod +x "$mock_bin/sleep"

output="$(MOCK_STATE_DIR="$mock_state" PATH="$mock_bin:$PATH" bash "$teardown_script" --resource-group existing-group)"
[[ "$output" == *'Read-only inventory for resource group: existing-group'* ]]
[[ "$output" == *'mock inventory'* ]]
[[ "$output" == *'Inventory completed. Deletion was not requested.'* ]]

: >"$mock_state/az-calls"
set +e
pending_output="$(MOCK_SCENARIO=pending MOCK_STATE_DIR="$mock_state" PATH="$mock_bin:$PATH" bash "$teardown_script" --resource-group existing-group --apply --confirm existing-group --timeout-seconds 1 2>&1)"
pending_status=$?
set -e

[[ "$pending_status" -eq 2 ]]
[[ "$pending_output" == *'Deletion is still pending for resource group: existing-group.'* ]]
[[ "$pending_output" == *'Do not rerun delete; poll az group exists --name "existing-group" until it returns false.'* ]]

delete_count=0
exists_count=0
while IFS= read -r az_call; do
  [[ "$az_call" == 'group delete '* ]] && ((delete_count += 1))
  [[ "$az_call" == 'group exists '* ]] && ((exists_count += 1))
done <"$mock_state/az-calls"
[[ "$delete_count" -eq 1 ]]
[[ "$exists_count" -eq 3 ]]
