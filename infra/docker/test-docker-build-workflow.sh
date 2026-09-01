#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflow="$repo_root/.github/workflows/docker-build-push.yml"
build_action='docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a # v7.3.0'

[[ "$(grep -c "$build_action" "$workflow")" -eq 2 ]]
grep -q 'full_sha=${EFFECTIVE_SHA}' "$workflow"
grep -Fq '^v[0-9]+\.[0-9]+\.[0-9]+(-(preview|insider)(\.[0-9]+)?)?$' "$workflow"
grep -q 'docker manifest inspect "$COMMIT_IMAGE"' "$workflow"
sed -n '236,246p' "$workflow" \
    | grep -q "if: github.event_name != 'pull_request'"
grep -q "if: steps.commit-image.outputs.exists != 'true'" "$workflow"
grep -q 'version="${GITHUB_REF_NAME#v}"' "$workflow"
grep -q 'minor="${version%.*}"' "$workflow"
grep -q 'IS_DISPATCH_PREVIEW:' "$workflow"
grep -q 'version="0.0.0-preview.${EFFECTIVE_SHA:0:7}"' "$workflow"
grep -q 'sha-$full_sha${suffix}' "$workflow"

echo "PASS: Docker release tags reuse commit images"
