#!/usr/bin/env bash
# Sync the local Lucia custom component to a remote Home Assistant server.
#
# Usage:
#   From repo root:
#     scripts/sync-lucia-to-ha.sh
#     scripts/sync-lucia-to-ha.sh --dry-run
#   From anywhere:
#     HA_REMOTE_HOST=192.168.1.198 HA_REMOTE_CONFIG=/config ./scripts/sync-lucia-to-ha.sh
#
# Configuration (env vars or .env in repo root):
#   HA_REMOTE_HOST     Remote host (IP or hostname). Required.
#   HA_REMOTE_USER     SSH user (default: root).
#   HA_REMOTE_CONFIG   Path to HA config dir on remote, e.g. /config or ~/.homeassistant (default: /config).
#   HA_SSH_PORT        SSH port (default: 22).
#   HA_SSH_KEY         Path to SSH private key (optional).
#
# After sync, restart Home Assistant or reload the Lucia integration so the new code is loaded.
#
# Examples:
#   HA_REMOTE_HOST=homeassistant.local HA_REMOTE_CONFIG=/config ./scripts/sync-lucia-to-ha.sh
#   HA_REMOTE_HOST=192.168.1.198 HA_REMOTE_USER=root HA_SSH_KEY=~/.ssh/ha_key ./scripts/sync-lucia-to-ha.sh
#   HA_REMOTE_HOST=pi.lan HA_REMOTE_CONFIG=/home/pi/.homeassistant ./scripts/sync-lucia-to-ha.sh --dry-run

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_DIR="$REPO_ROOT/custom_components/lucia"

# Load .env from repo root if present (optional)
if [[ -f "$REPO_ROOT/.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$REPO_ROOT/.env"
  set +a
fi
# Allow script-local .env (e.g. scripts/.env.ha) to override
if [[ -f "$SCRIPT_DIR/.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$SCRIPT_DIR/.env"
  set +a
fi

# Configurable options (env with defaults)
HA_REMOTE_HOST="${HA_REMOTE_HOST:-}"
HA_REMOTE_USER="${HA_REMOTE_USER:-root}"
HA_REMOTE_CONFIG="${HA_REMOTE_CONFIG:-/config}"
HA_SSH_PORT="${HA_SSH_PORT:-22}"
HA_SSH_KEY="${HA_SSH_KEY:-}"

DRY_RUN=false
while [[ $# -gt 0 ]]; do
  case $1 in
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    -h|--help)
      head -50 "$(basename "$0")" 2>/dev/null || head -50 "$0"
      echo ""
      echo "Environment: HA_REMOTE_HOST, HA_REMOTE_USER, HA_REMOTE_CONFIG, HA_SSH_PORT, HA_SSH_KEY"
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      exit 1
      ;;
  esac
done

if [[ -z "$HA_REMOTE_HOST" ]]; then
  echo "Error: HA_REMOTE_HOST is required."
  echo "Set it to the IP or hostname of your Home Assistant server, e.g.:"
  echo "  export HA_REMOTE_HOST=192.168.1.198"
  echo "  export HA_REMOTE_CONFIG=/config   # path to HA config on remote (default: /config)"
  echo "Or create scripts/.env or repo .env with HA_REMOTE_HOST=..."
  exit 1
fi

if [[ ! -d "$SOURCE_DIR" ]]; then
  echo "Error: Lucia custom component not found at $SOURCE_DIR"
  exit 1
fi

REMOTE_DIR="$HA_REMOTE_CONFIG/custom_components/lucia"
# Build SSH options for rsync -e (single string)
SSH_E_OPTS="-o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new -p $HA_SSH_PORT"
[[ -n "$HA_SSH_KEY" ]] && SSH_E_OPTS="$SSH_E_OPTS -i $HA_SSH_KEY"

RSYNC_OPTS=(-a -v --delete --exclude="__pycache__" --exclude="*.pyc" --exclude=".mypy_cache")
[[ "$DRY_RUN" == true ]] && RSYNC_OPTS+=(--dry-run --itemize-changes)

echo "Syncing Lucia integration to remote Home Assistant"
echo "  From:  $SOURCE_DIR"
echo "  To:    $HA_REMOTE_USER@$HA_REMOTE_HOST:$REMOTE_DIR"
echo "  Config path on remote: $HA_REMOTE_CONFIG"
[[ "$DRY_RUN" == true ]] && echo "  (dry run — no changes will be made)"
echo ""

# Ensure remote custom_components exists
if [[ "$DRY_RUN" != true ]]; then
  ssh -o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new -p "$HA_SSH_PORT" \
    ${HA_SSH_KEY:+-i "$HA_SSH_KEY"} \
    "$HA_REMOTE_USER@$HA_REMOTE_HOST" "mkdir -p $HA_REMOTE_CONFIG/custom_components"
fi

rsync "${RSYNC_OPTS[@]}" \
  -e "ssh $SSH_E_OPTS" \
  "$SOURCE_DIR/" \
  "$HA_REMOTE_USER@$HA_REMOTE_HOST:$REMOTE_DIR/"

echo ""
echo "Done. On the remote server, restart Home Assistant or reload the Lucia integration to load the new code."
echo "  (Settings → Devices & services → Lucia → Reload, or restart HA.)"
