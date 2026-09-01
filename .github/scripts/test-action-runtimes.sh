#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflows="$repo_root/.github/workflows"

approved_actions=(
    actions/attest-build-provenance@977bb373ede98d70efdf65b84cb5f73e068dcc2a
    actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd
    actions/github-script@ed597411d8f924073f98dfc5c65a23a2325f34cd
    actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68
    actions/setup-node@820762786026740c76f36085b0efc47a31fe5020
    actions/setup-python@ece7cb06caefa5fff74198d8649806c4678c61a1
    aquasecurity/trivy-action@57a97c7e7821a5776cebc9bb87c984fa69cba8f1
    azure/setup-helm@9bc31f4ebc9c6b171d7bfbaa5d006ae7abdb4310
    DavidAnson/markdownlint-cli2-action@21c1be1b93ad9ed58fa840aacc3f279cde2a72ff
    docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a
    docker/login-action@650006c6eb7dba73a995cc03b0b2d7f5ca915bee
    docker/metadata-action@80c7e94dd9b9319bd5eb7a0e0fe9291e23a2a2e9
    docker/setup-buildx-action@d7f5e7f509e45cec5c76c4d5afdd7de93d0b3df5
    docker/setup-qemu-action@06116385d9baf250c9f4dcb4858b16962ea869c3
    github/codeql-action/upload-sarif@dc73d59c2d7bd4f8194098a91219eeee6d8a1719
    hacs/action@dcb30e72781db3f207d5236b861172774ab0b485
    home-assistant/actions/hassfest@868e6cb4607727d764341a158d98872cd63fa658
)

mapfile -t used_actions < <(
    grep -RhE 'uses:[[:space:]]+[^[:space:]#]+' "$workflows" \
        | sed -E 's/.*uses:[[:space:]]+([^[:space:]#]+).*/\1/' \
        | grep -v '^\./' \
        | sort -u
)
for used_action in "${used_actions[@]}"; do
    approved=false
    for approved_action in "${approved_actions[@]}"; do
        if [[ "$used_action" == "$approved_action" ]]; then
            approved=true
            break
        fi
    done
    if [[ "$approved" != true ]]; then
        echo "Action is not approved for the Node 24 runner: $used_action" >&2
        exit 1
    fi
done

echo "PASS: workflow actions match the Node 24 allowlist"
