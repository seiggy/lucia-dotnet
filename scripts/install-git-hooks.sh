#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

chmod +x .githooks/pre-commit
chmod +x scripts/validate-ha-commit.sh
chmod +x scripts/check_utf8_no_bom.py

git config core.hooksPath .githooks

echo "Installed repository git hooks."
echo "core.hooksPath=$(git config --get core.hooksPath)"
