#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflows="$repo_root/.github/workflows"

reject_node20_action() {
    if grep -R -q "$1" "$workflows"; then
        echo "Node 20 action remains: $1" >&2
        exit 1
    fi
}

reject_node20_action 'actions/setup-dotnet@67a3573c9'
reject_node20_action 'actions/setup-node@49933ea52'
reject_node20_action 'DavidAnson/markdownlint-cli2-action@07035fd05'
reject_node20_action 'docker/build-push-action@10e90e364'

echo "PASS: JavaScript actions use Node 24 releases"
