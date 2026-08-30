#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
teardown_script="$script_dir/teardown-mvp.sh"
mock_bin="$(mktemp -d)"
trap 'rm -rf "$mock_bin"' EXIT

cat >"$mock_bin/az" <<'EOF'
#!/usr/bin/env bash
case "$1 $2" in
  'group exists') printf '\357\273\277TrUe\r\n' ;;
  'resource list') printf 'mock inventory\n' ;;
  *) exit 1 ;;
esac
EOF
chmod +x "$mock_bin/az"

output="$(PATH="$mock_bin:$PATH" bash "$teardown_script" --resource-group existing-group)"
[[ "$output" == *'Read-only inventory for resource group: existing-group'* ]]
[[ "$output" == *'mock inventory'* ]]
[[ "$output" == *'Inventory completed. Deletion was not requested.'* ]]
