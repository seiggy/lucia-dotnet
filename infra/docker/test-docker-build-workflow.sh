#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workflow="$repo_root/.github/workflows/docker-build-push.yml"
build_action='docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a # v7.3.0'

[[ "$(grep -c "$build_action" "$workflow")" -eq 2 ]]
grep -q 'full_sha=${EFFECTIVE_SHA}' "$workflow"
grep -Fq '^v[0-9]+\.[0-9]+\.[0-9]+(-(preview|insider)(\.[0-9]+)?)?$' "$workflow"
grep -q 'docker manifest inspect "$COMMIT_IMAGE"' "$workflow"
grep -A2 -F 'id: commit-image' "$workflow" \
    | grep -Fq "if: github.event_name != 'pull_request'"
grep -q "if: steps.commit-image.outputs.exists != 'true'" "$workflow"
grep -q 'version="${GITHUB_REF_NAME#v}"' "$workflow"
grep -q 'minor="${version%.*}"' "$workflow"
grep -q 'IS_DISPATCH_PREVIEW:' "$workflow"
grep -Fq 'if [[ "$IS_RELEASE_TAG" != "true" && "$IS_DISPATCH_PREVIEW" != "true" ]]; then' "$workflow"
grep -q 'source_tag="sha-$full_sha"' "$workflow"
grep -q 'VERSIONED_BUILD:' "$workflow"

python3 - "$workflow" <<'PY'
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import textwrap

workflow = Path(sys.argv[1]).read_text()

def script(name):
    step = workflow.split(f"      - name: {name}\n", 1)[1].split("\n      - name:", 1)[0]
    lines = []
    for line in step.split("        run: |\n", 1)[1].splitlines():
        if line.strip() and not line.startswith("          "):
            break
        lines.append(line)
    return textwrap.dedent("\n".join(lines))

with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    docker = root / "docker"
    docker.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$DOCKER_LOG"\nexit 0\n')
    docker.chmod(0o755)
    environment = {
        **os.environ, "PATH": f"{root}:{os.environ['PATH']}",
        "GITHUB_OUTPUT": str(root / "output"), "DOCKER_LOG": str(root / "docker.log"),
        "COMMIT_IMAGE": "test:sha-abc", "VERSIONED_BUILD": "true",
    }
    subprocess.run(["bash", "-c", script("Check for existing commit image")], env=environment, check=True)
    assert (root / "output").read_text().strip() == "exists=false"
    assert not (root / "docker.log").exists()
    environment["VERSIONED_BUILD"] = "false"
    (root / "output").write_text("")
    subprocess.run(["bash", "-c", script("Check for existing commit image")], env=environment, check=True)
    assert (root / "output").read_text().strip() == "exists=true"
    (root / "docker.log").write_text("")
    environment.update(IS_RELEASE_TAG="true", IS_DISPATCH_PREVIEW="false",
                       EFFECTIVE_SHA="abc1234", GITHUB_REF_NAME="v1.5.1")
    promotion = script("Promote channels if build is still current").replace("${{ github.repository }}", "seiggy/lucia-dotnet")
    subprocess.run(["bash", "-c", promotion], env=environment, check=True)
    commands = (root / "docker.log").read_text().splitlines()
    assert commands and all("sha-abc" not in command for command in commands)
    assert any("-t seiggy/lucia-agenthost:latest seiggy/lucia-agenthost:1.5.1" in command for command in commands)
PY
echo "PASS: Docker releases embed their version and promotion preserves that metadata"
