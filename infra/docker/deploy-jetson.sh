#!/usr/bin/env bash
# deploy-jetson.sh — One-command non-voice deploy for NVIDIA Jetson (ARM64)
#
# Run this DIRECTLY ON THE JETSON (or from a host on the same LAN with SSH access).
#
# Usage (on Jetson):
#   cd lucia-dotnet/infra/docker
#   ./deploy-jetson.sh
#   ./deploy-jetson.sh --rebuild        # force no-cache build
#   ./deploy-jetson.sh --wipe           # tear down + remove volumes first
#   ./deploy-jetson.sh --pull           # git pull before build
#   ./deploy-jetson.sh --pull --rebuild # fresh pull + clean build
#
# What it deploys (non-voice topology):
#   lucia-redis-jetson   Redis 8.2-alpine   — task persistence / session state
#   lucia-mongo-jetson   MongoDB 8.0.5      — traces + config storage
#   lucia-jetson         AgentHost ARM64    — main API, no speech pipeline
#
# After the stack is running, open http://<jetson-ip>:7233 to run the setup wizard.
# AgentHost health endpoint: http://<jetson-ip>:7233/health

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.jetson.yml"

PULL=0
REBUILD=0
WIPE=0

while [[ $# -gt 0 ]]; do
  case $1 in
    --pull)    PULL=1;    shift ;;
    --rebuild) REBUILD=1; shift ;;
    --wipe)    WIPE=1;    shift ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: $0 [--pull] [--rebuild] [--wipe]"
      exit 1
      ;;
  esac
done

# Verify arch
ARCH="$(uname -m)"
if [[ "$ARCH" != "aarch64" ]]; then
  echo "WARNING: Expected aarch64 (ARM64) but got '$ARCH'."
  echo "         This compose file builds ARM64 images. Proceed with caution."
fi

# Optionally pull latest repo state
if [[ $PULL -eq 1 ]]; then
  echo "==> Pulling latest code..."
  REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
  git -C "$REPO_ROOT" pull --ff-only
fi

compose() {
  docker compose -f "$COMPOSE_FILE" "$@"
}

# Optionally wipe volumes (destructive)
if [[ $WIPE -eq 1 ]]; then
  echo "==> Wiping Lucia Jetson stack (containers + volumes)..."
  compose down -v --remove-orphans || true
fi

# Build image (with or without cache)
if [[ $REBUILD -eq 1 ]]; then
  echo "==> Building lucia:jetson image (no-cache)..."
  compose build --no-cache lucia
else
  echo "==> Building lucia:jetson image..."
  compose build lucia
fi

# Start the stack
echo "==> Starting non-voice Jetson stack..."
compose up -d

# Wait briefly then show status
sleep 3
echo ""
echo "==> Container status:"
compose ps

# Detect Jetson LAN IP
JETSON_IP=""
if command -v hostname &>/dev/null; then
  JETSON_IP=$(hostname -I 2>/dev/null | awk '{for(i=1;i<=NF;i++){if($i!~/^127\./){print $i;exit}}}')
fi
JETSON_IP="${JETSON_IP:-<jetson-ip>}"

echo ""
echo "---"
echo "Lucia dashboard:       http://${JETSON_IP}:7233"
echo "AgentHost health:      http://${JETSON_IP}:7233/health"
echo "Home Assistant Lucia:  Settings → Integrations → Lucia"
echo "  Agent Repository URL: http://${JETSON_IP}:7233"
echo "---"
echo "View logs:   docker compose -f $COMPOSE_FILE logs -f lucia"
echo "Stop stack:  docker compose -f $COMPOSE_FILE down"
echo ""
