#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

STAGED_FILES="$(git diff --cached --name-only --diff-filter=ACMR)"

if [[ -z "$STAGED_FILES" ]]; then
  echo "[pre-commit] No staged files; skipping HA validations."
  exit 0
fi

echo "[pre-commit] Validating UTF-8 (no BOM) for hacs.json..."
if command -v python3 >/dev/null 2>&1; then
  PYTHON_CMD=python3
elif command -v python >/dev/null 2>&1; then
  PYTHON_CMD=python
else
  echo "[pre-commit] Python (python3 or python) is required to validate hacs.json encoding."
  exit 1
fi

"$PYTHON_CMD" scripts/check_utf8_no_bom.py hacs.json

if ! grep -Eq '^(custom_components/lucia/|hacs\.json$)' <<< "$STAGED_FILES"; then
  echo "[pre-commit] No Lucia/HACS files staged; skipping ruff + hassfest."
  exit 0
fi

echo "[pre-commit] Running ruff..."
if ! command -v ruff >/dev/null 2>&1; then
  echo "[pre-commit] ruff is required to run Python style checks."
  echo "[pre-commit] Install it with e.g.: pipx install ruff  # or: python3 -m pip install --user ruff"
  exit 1
fi

ruff check custom_components/lucia

if ! command -v docker >/dev/null 2>&1; then
  echo "[pre-commit] Docker is required to run hassfest."
  exit 1
fi

echo "[pre-commit] Running hassfest..."
docker run --rm -v "$REPO_ROOT":/github/workspace ghcr.io/home-assistant/hassfest

echo "[pre-commit] Validation complete."
