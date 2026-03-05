#!/usr/bin/env bash
# Deploy Lucia using the sidecar compose (alongside Open Web UI, remote Home Assistant).
#
# Usage:
#   ./deploy-lucia.sh [--env-file .env] [--wipe] [--rebuild]
#   From repo root: infra/docker/deploy-lucia.sh
#   From infra/docker: ./deploy-lucia.sh
#
# Options:
#   --wipe     Stop containers and remove all Lucia volumes (Redis + MongoDB).
#              Next up will start with empty DBs; you will need to run setup again.
#   --rebuild  Build images with --no-cache before starting.
#
# If .env is missing, copies .env.lucia.example to .env and prompts you to set
# HomeAssistant__AccessToken (and optionally other vars) before re-running.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ENV_FILE=".env"
WIPE=0
REBUILD=0
while [[ $# -gt 0 ]]; do
  case $1 in
    --env-file)
      ENV_FILE="$2"
      shift 2
      ;;
    --wipe)
      WIPE=1
      shift
      ;;
    --rebuild)
      REBUILD=1
      shift
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: $0 [--env-file .env] [--wipe] [--rebuild]"
      exit 1
      ;;
  esac
done

if [[ ! -f "$ENV_FILE" ]]; then
  if [[ -f .env.lucia.example ]]; then
    cp .env.lucia.example "$ENV_FILE"
    echo "Created $ENV_FILE from .env.lucia.example."
    echo "Edit $ENV_FILE and set at least: HomeAssistant__AccessToken"
    echo "Then run: $0 --env-file $ENV_FILE"
    exit 1
  else
    echo "Missing $ENV_FILE and .env.lucia.example not found in $SCRIPT_DIR"
    exit 1
  fi
fi

COMPOSE_CMD="docker compose -f docker-compose.yml -f docker-compose.lucia-sidecar.yml --env-file $ENV_FILE"

if [[ $WIPE -eq 1 ]]; then
  echo "Wiping Lucia data: stopping containers and removing volumes (lucia-redis-data, lucia-mongo-data)..."
  $COMPOSE_CMD down -v
  echo "Lucia data wiped."
fi

if [[ $REBUILD -eq 1 ]]; then
  echo "Rebuilding Lucia image (no cache)..."
  $COMPOSE_CMD build --no-cache
fi

echo "Starting Lucia (sidecar mode) with env file: $ENV_FILE"
$COMPOSE_CMD up -d

# Try to determine host IP for Agent Repository URL (best effort)
HA_REPO_IP=""
if command -v hostname &>/dev/null; then
  # Prefer first non-loopback IPv4 from hostname -I (Linux)
  HA_REPO_IP=$(hostname -I 2>/dev/null | awk '{for(i=1;i<=NF;i++){if($i!~/^127\./){print $i;exit}}}')
fi
if [[ -z "$HA_REPO_IP" ]] && command -v ip &>/dev/null; then
  HA_REPO_IP=$(ip route get 1 2>/dev/null | awk '{print $7; exit}')
fi
if [[ -z "$HA_REPO_IP" ]]; then
  HA_REPO_IP="<this-machine-ip>"
fi

echo ""
echo "---"
echo "Lucia dashboard:    http://localhost:7233"
echo "Agent Repository URL (use in Home Assistant): http://${HA_REPO_IP}:7233"
echo "---"
echo "Next steps:"
echo "  1. Open http://localhost:7233 and complete setup if needed (or verify config)."
echo "  2. On Home Assistant: Add Integration → Lucia → Agent Repository URL = http://${HA_REPO_IP}:7233"
echo "  3. Create a long-lived access token in HA (Profile → Long-Lived Access Tokens) and set HomeAssistant__AccessToken in $ENV_FILE if using headless config."
echo ""
