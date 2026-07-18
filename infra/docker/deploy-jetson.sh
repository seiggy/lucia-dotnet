#!/usr/bin/env bash
# deploy-jetson.sh — Deploy the pre-built voice AgentHost to a Jetson Orin Nano Super.
#
# Runs ON THE JETSON (aarch64). NEVER builds on-device.
#
# docker-compose.jetson-voice.yml is self-contained: PostgreSQL + Redis + AgentHost
# in one fixed Compose project (lucia-voice) on one default bridge network.
# Postgres and Redis are started in Phase 1 and stay running through AgentHost
# upgrades. Existing Mongo volumes are out-of-band legacy data; never touched here.
#
# Migration is image-based and fully reversible:
#   record running AgentHost image config ID (immutable sha256) for rollback
#   Phase 1: start postgres + redis; bootstrap all 3 DBs idempotently
#   Phase 2: compose up AgentHost with the new image
#   verify /health + Wyoming /api/wyoming/status CUDA-EP
#   on failure: attempt AgentHost rollback to prior image; preserve evidence on error
#   on success: rollback record kept so --rollback remains usable
#
# Usage:
#   ./deploy-jetson.sh --image lucia-agenthost-voice@sha256:<64hex>   # registry digest
#   ./deploy-jetson.sh --image sha256:<64hex>                         # local image ID
#   ./deploy-jetson.sh --image <ref> --dry-run                        # no mutations
#   ./deploy-jetson.sh --image <ref> --bootstrap                      # first-run: skip Wyoming CUDA gate
#   ./deploy-jetson.sh --rollback                                      # redeploy prior image
#
# Options:
#   --image REF        Immutable image ref: name@sha256:<64hex> or sha256:<64hex>.
#                      Full 64-hex required; abbreviated IDs rejected. Required to deploy.
#   --rollback         Redeploy the AgentHost image recorded before the last deployment.
#   --dry-run          Print planned actions + compose config (password redacted); no mutations.
#   --health-timeout N Seconds to wait for /health + Wyoming CUDA-EP (default 180).
#   --bootstrap        First-run mode: skip Wyoming CUDA gate; keep AgentHost running when
#                      exact HA configuration wait marker appears in logs. Requires /health
#                      PASS. Only valid with no prior AgentHost container in any state
#                      (running/stopped/exited/unhealthy). Fresh means no container object.
#                      Exit 0 = verified waiting state ("BOOTSTRAP REQUIRED"). Rollback
#                      record not retained. Combine with --dry-run to preview.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VOICE_COMPOSE="$SCRIPT_DIR/docker-compose.jetson-voice.yml"
DEPLOY_PROJECT="lucia-voice"
ROLLBACK_FILE="$SCRIPT_DIR/.rollback-image"

IMAGE_NAME=""
ROLLBACK=0
DRY_RUN=0
BOOTSTRAP=0
HEALTH_TIMEOUT=180

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image)          IMAGE_NAME="${2:-}"; shift 2 ;;
    --rollback)       ROLLBACK=1; shift ;;
    --dry-run)        DRY_RUN=1; shift ;;
    --bootstrap)      BOOTSTRAP=1; shift ;;
    --health-timeout) HEALTH_TIMEOUT="${2:-}"; shift 2 ;;
    *) echo "ERROR: unknown option: $1" >&2; exit 2 ;;
  esac
done

die() { echo "ERROR: $*" >&2; exit 1; }

# ---------------------------------------------------------------------------
# Npgsql password quoting: wrap in single quotes, doubling embedded single quotes.
# Safely handles ; " \ = and all other special chars in Npgsql connection strings.
# See: https://www.npgsql.org/doc/connection-string-parameters.html (Quoted values)
# ---------------------------------------------------------------------------
_npgsql_quote() {
  printf "'%s'" "$(printf '%s' "$1" | sed "s/'/\\'\\'/g")"
}

# Build pre-escaped Npgsql connection strings. Exports CS_LUCIACONFIG, CS_LUCIATRACES,
# CS_LUCIATASKS. These are NEVER echoed or logged; they contain the password.
_build_cs_vars() {
  local pw_quoted; pw_quoted="$(_npgsql_quote "$POSTGRES_PASSWORD")"
  local base="Host=lucia-postgres;Port=5432;Username=postgres;Password=${pw_quoted}"
  export CS_LUCIACONFIG="${base};Database=luciaconfig"
  export CS_LUCIATRACES="${base};Database=luciatraces"
  export CS_LUCIATASKS="${base};Database=luciatasks"
}

# ---------------------------------------------------------------------------
# Preflight (read-only checks). Never mutates anything.
# ---------------------------------------------------------------------------
preflight() {
  echo "==> Preflight (read-only checks)"
  local arch; arch="$(uname -m)"
  [[ "$arch" == "aarch64" ]] || die "expected aarch64 (Jetson), got '$arch'. Never deploy voice off-target."
  docker info --format '{{json .Runtimes}}' 2>/dev/null | grep -q '"nvidia"' \
    || die "NVIDIA Container Runtime not found. GPU passthrough required for the CUDA EP."
  [[ -n "${POSTGRES_PASSWORD:-}" ]] \
    || die "POSTGRES_PASSWORD is not set. Export it or source a .env file before deploying."
  echo "    aarch64 + NVIDIA runtime + POSTGRES_PASSWORD: OK"
}

# ---------------------------------------------------------------------------
# Immutable image ID of the running AgentHost, or empty.
# Uses .Image (config digest, immutable), NOT .Config.Image (mutable start ref).
# ---------------------------------------------------------------------------
_current_agenthost_image() {
  local cid
  cid="$(docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" ps -q lucia-agenthost-voice 2>/dev/null \
         | head -n 1 || true)"
  [[ -n "$cid" ]] && docker inspect --format '{{.Image}}' "$cid" 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# Wait up to 120s for postgres and redis to become healthy.
# ---------------------------------------------------------------------------
_wait_data_services() {
  echo "    waiting for postgres + redis to become healthy..."
  local deadline=$(( SECONDS + 120 ))
  while (( SECONDS < deadline )); do
    local pg_cid rd_cid pg_h rd_h
    pg_cid="$(docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" ps -q lucia-postgres  2>/dev/null | head -n 1 || true)"
    rd_cid="$(docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" ps -q lucia-redis     2>/dev/null | head -n 1 || true)"
    pg_h="none"; rd_h="none"
    [[ -n "$pg_cid" ]] && pg_h="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$pg_cid" 2>/dev/null || echo none)"
    [[ -n "$rd_cid" ]] && rd_h="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$rd_cid" 2>/dev/null || echo none)"
    [[ "$pg_h" == "healthy" && "$rd_h" == "healthy" ]] && { echo "    postgres + redis: healthy"; return 0; }
    sleep 3
  done
  die "postgres/redis did not become healthy within 120s. Aborting before AgentHost start."
}

# ---------------------------------------------------------------------------
# Idempotent DB bootstrap. Ensures luciaconfig, luciatraces, luciatasks exist.
# Safe on both fresh and reused volumes — only creates what is missing.
# ---------------------------------------------------------------------------
_ensure_databases() {
  echo "==> Ensuring databases exist (idempotent)"
  local pg_cid
  pg_cid="$(docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" ps -q lucia-postgres 2>/dev/null \
            | head -n 1 || true)"
  [[ -n "$pg_cid" ]] || die "postgres container not found after startup"
  for db in luciaconfig luciatraces luciatasks; do
    docker exec -i "$pg_cid" psql -U postgres -tc \
        "SELECT 1 FROM pg_database WHERE datname='${db}'" 2>/dev/null \
        | grep -q 1 \
      || docker exec -i "$pg_cid" createdb -U postgres "${db}"
  done
  echo "    databases: luciaconfig luciatraces luciatasks — ready"
}

# ---------------------------------------------------------------------------
# Rollback: redeploy the image recorded before the last deployment.
# ---------------------------------------------------------------------------
do_rollback() {
  echo "==> Rollback"
  [[ -f "$ROLLBACK_FILE" ]] || die "no rollback record at $ROLLBACK_FILE"
  [[ -n "${POSTGRES_PASSWORD:-}" ]] \
    || die "POSTGRES_PASSWORD is not set. Required to bring up the AgentHost."
  local prior_image
  prior_image="$(sed -n '1p' "$ROLLBACK_FILE")"
  [[ -n "$prior_image" ]] || die "rollback record is empty (fresh deploy — no prior image to restore)"
  echo "    rolling back AgentHost to: $prior_image"
  _build_cs_vars
  LUCIA_IMAGE="$prior_image" docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" \
      up -d --no-build --no-deps lucia-agenthost-voice \
    || die "rollback compose up failed. Postgres + Redis untouched. Manual: LUCIA_IMAGE='$prior_image' docker compose -p $DEPLOY_PROJECT -f $VOICE_COMPOSE up -d --no-deps lucia-agenthost-voice"
  rm -f "$ROLLBACK_FILE"
  echo "    rollback complete. Postgres + Redis were never touched."
}

# ---------------------------------------------------------------------------
# Live rollback helper: called on compose-up or verify failure.
# Preserves rollback file if restore fails so operator can retry manually.
# ---------------------------------------------------------------------------
_rollback_live() {
    local prior_image
    prior_image="$(sed -n '1p' "$ROLLBACK_FILE" 2>/dev/null || true)"
    if [[ -n "$prior_image" ]]; then
        echo "    restoring AgentHost to prior image: $prior_image" >&2
        if LUCIA_IMAGE="$prior_image" docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" \
               up -d --no-build --no-deps lucia-agenthost-voice 2>&1; then
            rm -f "$ROLLBACK_FILE"
            echo "    rollback complete. Postgres + Redis were never touched." >&2
        else
            # Evidence deliberately preserved — do NOT rm here
            echo "    ERROR: rollback compose up failed — evidence preserved at $ROLLBACK_FILE" >&2
            echo "    Manual: LUCIA_IMAGE='$prior_image' docker compose -p $DEPLOY_PROJECT -f $VOICE_COMPOSE up -d --no-deps lucia-agenthost-voice" >&2
        fi
    else
        echo "    fresh deploy (no prior image); stopping and removing failed AgentHost." >&2
        docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" stop lucia-agenthost-voice 2>/dev/null || true
        docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" rm -f lucia-agenthost-voice 2>/dev/null || true
        rm -f "$ROLLBACK_FILE"
        echo "    Postgres + Redis were never touched." >&2
    fi
}

# ---------------------------------------------------------------------------
# Verify the freshly started AgentHost: /health + Wyoming status API CUDA-EP.
# ---------------------------------------------------------------------------
verify_new() {
    echo "==> Verifying /health + Wyoming status API (timeout ${HEALTH_TIMEOUT}s)"
    local deadline=$(( SECONDS + HEALTH_TIMEOUT )) healthy=0
    while (( SECONDS < deadline )); do
        if curl -fsS --max-time 5 http://localhost:7233/health >/dev/null 2>&1 \
           || wget -q --timeout=5 -O- http://localhost:7233/health >/dev/null 2>&1; then
            healthy=1; break
        fi
        sleep 3
    done
    [[ "$healthy" -eq 1 ]] || { echo "    /health did not become ready within ${HEALTH_TIMEOUT}s" >&2; return 1; }
    echo "    /health: PASS"

    local status_json="" status_deadline=$(( SECONDS + 30 ))
    while (( SECONDS < status_deadline )); do
        status_json="$(curl -fsS --max-time 5 http://localhost:7233/api/wyoming/status 2>/dev/null \
                        || wget -q --timeout=5 -O- http://localhost:7233/api/wyoming/status 2>/dev/null \
                        || true)"
        [[ -n "$status_json" ]] && break
        sleep 2
    done

    if [[ -z "$status_json" ]]; then
        echo "    CUDA EP: FAIL — /api/wyoming/status did not respond" >&2
        return 1
    fi

    # The response is the real /api/wyoming/status DTO: a NESTED object whose siblings
    # (stt, vad, wakeWord, diarization, speechEnhancement, customWakeWords, onnxProvider,
    # configured) are themselves objects/arrays. curl -fsS gates HTTP 2xx; the ASP.NET
    # serializer produces well-formed JSON (trusted). Gates below assert only the CUDA-EP:
    #   _prov — isolates the FLAT onnxProvider object. [^{}]* is safe because its only
    #           container value is a []-array (not a nested {}). Requiring the object to be
    #           followed by ',' or '}' rejects garbage wedged after its closing brace.
    #           BASH_REMATCH[1] is that object; _sel/_acc are matched INSIDE it only, so one
    #           component's value can never cross-pair with another component's.
    #   _sel  — "selected":"CUDAExecutionProvider" (the trailing quote bounds the value, so
    #           "CUDA ExecutionProvider" / ...ProviderX are rejected; CUDA inside available[]
    #           is not preceded by "selected": and so is not a false positive).
    #   _acc  — "isAccelerated":true followed by '}' (last key) or ',"' (next quoted key),
    #           rejecting "true", t r u e, truefalse, false, and bare-word garbage.
    local _prov='"onnxProvider"[[:space:]]*:[[:space:]]*(\{[^{}]*\})[[:space:]]*[,}]'
    local _sel='"selected"[[:space:]]*:[[:space:]]*"CUDAExecutionProvider"'
    local _acc='"isAccelerated"[[:space:]]*:[[:space:]]*true[[:space:]]*(\}|,[[:space:]]*")'
    if [[ "$status_json" =~ $_prov ]]; then
        local _prov_obj="${BASH_REMATCH[1]}"
        if [[ "$_prov_obj" =~ $_sel ]] && [[ "$_prov_obj" =~ $_acc ]]; then
            echo "    CUDA EP: PASS"
            return 0
        fi
    fi
    echo "    CUDA EP: FAIL — selected provider is not CUDA or isAccelerated is false/missing" >&2
    printf '    Raw status: %s\n' "$status_json" >&2
    return 1
}

# ---------------------------------------------------------------------------
# Bootstrap verify: /health PASS + exact HA configuration wait-marker in logs.
# Called only when --bootstrap is set (fresh first-run, no prior AgentHost).
# ---------------------------------------------------------------------------
verify_bootstrap() {
    echo "==> Bootstrap: verifying /health (timeout ${HEALTH_TIMEOUT}s)"
    local deadline=$(( SECONDS + HEALTH_TIMEOUT )) healthy=0
    while (( SECONDS < deadline )); do
        if curl -fsS --max-time 5 http://localhost:7233/health >/dev/null 2>&1 \
           || wget -q --timeout=5 -O- http://localhost:7233/health >/dev/null 2>&1; then
            healthy=1; break
        fi
        sleep 3
    done
    [[ "$healthy" -eq 1 ]] || { echo "    /health did not become ready within ${HEALTH_TIMEOUT}s" >&2; return 1; }
    echo "    /health: PASS"

    echo "==> Bootstrap: checking AgentHost logs for HA configuration wait marker (timeout ${BOOTSTRAP_LOG_TIMEOUT:-30}s)"
    local cid
    cid="$(docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" ps -q lucia-agenthost-voice 2>/dev/null || true)"
    [[ -n "$cid" ]] || { echo "    AgentHost container not found" >&2; return 1; }

    local log_deadline=$(( SECONDS + ${BOOTSTRAP_LOG_TIMEOUT:-30} )) found=0
    while (( SECONDS < log_deadline )); do
        local logs; logs="$(docker logs "$cid" 2>&1 || true)"
        # Direct glob: no pipe, so no SIGPIPE under set -o pipefail on large logs.
        # Logger prefix/suffix on the same line are permitted; split cross-line and
        # truncated forms are rejected because the full contiguous literal must be present.
        [[ "$logs" == *'Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)'* ]] \
            && { found=1; break; }
        sleep 2
    done
    [[ "$found" -eq 1 ]] || { echo "    HA configuration wait marker not found in AgentHost logs — unexpected state" >&2; return 1; }
    echo "    HA wait marker: confirmed (intentional first-run state)"
    return 0
}

# ---------------------------------------------------------------------------
# Interruption handler: attempt safe AgentHost rollback exactly once.
# Set only after the rollback record has been written (mutations have begun).
# ---------------------------------------------------------------------------
_int_fired=0
_agenthost_mutated=0
_handle_interrupt() {
    [[ $_int_fired -eq 0 ]] || return
    _int_fired=1
    trap '' INT TERM HUP    # prevent re-entry during rollback
    echo "" >&2
    echo "==> Signal received — attempting safe AgentHost rollback" >&2
    if [[ $_agenthost_mutated -eq 1 ]]; then
        _rollback_live
    else
        echo "    no AgentHost mutation yet — cleaning up rollback record" >&2
        rm -f "$ROLLBACK_FILE"
    fi
    exit 130
}

# ---------------------------------------------------------------------------
# Deploy
# ---------------------------------------------------------------------------
do_deploy() {
  [[ -n "$IMAGE_NAME" ]] || die "--image is required (name@sha256:<64hex> or sha256:<64hex>)"
  # Accept only two immutable forms. Mutable tags and abbreviated IDs are rejected.
  if [[ ! "$IMAGE_NAME" =~ @sha256:[0-9a-f]{64}$ ]] && [[ ! "$IMAGE_NAME" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    die "--image must be a full registry digest (name@sha256:<64hex>) or full local image ID (sha256:<64hex>). Got '$IMAGE_NAME'. No mutable tags; no abbreviated IDs."
  fi
  local arch_info
  arch_info="$(docker image inspect --format '{{.Os}}/{{.Architecture}}' "$IMAGE_NAME" 2>/dev/null)" \
    || die "image '$IMAGE_NAME' not present locally. Load it first: docker load < lucia-voice.tar"
  [[ "$arch_info" == "linux/arm64" ]] || die "image arch is '${arch_info:-inspect-failed}', expected linux/arm64. Refusing non-ARM64 image on Jetson."

  # Export LUCIA_IMAGE once; all subsequent compose invocations use it.
  export LUCIA_IMAGE="$IMAGE_NAME"

  if [[ $DRY_RUN -eq 1 ]]; then
    echo "=== DRY RUN (no changes) ==="
    [[ $BOOTSTRAP -eq 1 ]] && echo "Mode:         BOOTSTRAP (Wyoming CUDA gate replaced by HA wait-marker check; no prior AgentHost allowed)"
    echo "Image:        $IMAGE_NAME"
    echo "Compose:      $VOICE_COMPOSE"
    echo "Project:      $DEPLOY_PROJECT"
    echo "--- compose config (password redacted) ---"
    CS_LUCIACONFIG="Host=lucia-postgres;Port=5432;Database=luciaconfig;Username=postgres;Password=<REDACTED>" \
    CS_LUCIATRACES="Host=lucia-postgres;Port=5432;Database=luciatraces;Username=postgres;Password=<REDACTED>" \
    CS_LUCIATASKS="Host=lucia-postgres;Port=5432;Database=luciatasks;Username=postgres;Password=<REDACTED>" \
    POSTGRES_PASSWORD="<REDACTED>" \
    docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" config
    exit 0
  fi

  preflight

  # Build safe Npgsql connection strings BEFORE any Compose command. The compose
  # file marks CS_LUCIACONFIG/TRACES/TASKS as required (${CS_*:?...}), so even the
  # read-only `compose ps` used to capture the prior image fails when they are unset
  # — which would silently lose the prior .Image and break rollback. (CS_* hold the
  # password and are never echoed/logged.)
  _build_cs_vars

  # Record immutable image config ID (.Image) of any currently-running AgentHost.
  # .Image is the sha256 config digest; .Config.Image is the mutable start ref.
  local prior_image
  prior_image="$(_current_agenthost_image || true)"
  if [[ -n "$prior_image" ]]; then
    echo "==> Current AgentHost: $prior_image (preserved for --rollback)"
  else
    echo "==> No existing AgentHost (fresh deploy)"
  fi

  # Bootstrap guard: query Docker labels directly to find any AgentHost container in any
  # state (running/stopped/exited/unhealthy). Does not depend on image inspection success.
  # Fail-closed: if Docker is unavailable the check itself fails and bootstrap is rejected.
  if [[ $BOOTSTRAP -eq 1 ]]; then
    local _bs_cids _bs_rc=0
    _bs_cids="$(docker ps -aq \
      --filter "label=com.docker.compose.project=${DEPLOY_PROJECT}" \
      --filter "label=com.docker.compose.service=lucia-agenthost-voice" 2>/dev/null)" || _bs_rc=$?
    [[ $_bs_rc -ne 0 ]] && die "--bootstrap: could not check for existing AgentHost containers (Docker daemon error). Refusing to proceed."
    [[ -n "$_bs_cids" ]] && die "--bootstrap rejected: existing AgentHost container detected (any state: running/stopped/exited/unhealthy). Bootstrap requires a fully fresh environment. Remove it first: docker compose -p $DEPLOY_PROJECT -f $VOICE_COMPOSE rm -f lucia-agenthost-voice"
  fi

  # Write rollback record before any mutation.
  printf '%s\n' "${prior_image}" > "$ROLLBACK_FILE" \
    || die "could not write rollback record to $ROLLBACK_FILE"

  # Interruption trap: active from here until deployment completes.
  trap '_handle_interrupt' INT TERM HUP

  # Phase 1: start data services (postgres + redis).
  echo "==> Starting data services (project: $DEPLOY_PROJECT)"
  docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" \
      up -d --no-build --no-deps lucia-postgres lucia-redis \
    || { echo "ERROR: data services failed to start — cleaning up" >&2
         rm -f "$ROLLBACK_FILE"; trap - INT TERM HUP; exit 1; }

  _wait_data_services

  # Idempotent DB bootstrap — creates only the databases that are missing.
  _ensure_databases

  # Phase 2: start AgentHost. From this point, rollback is the recovery path.
  _agenthost_mutated=1
  echo "==> Starting AgentHost (image: $IMAGE_NAME)"
  docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" \
      up -d --no-build --no-deps lucia-agenthost-voice \
    || { echo "ERROR: docker compose up FAILED — rolling back" >&2; _rollback_live; exit 1; }

  # Verify /health + Wyoming CUDA-EP (normal) or HA configuration wait marker (bootstrap).
  if [[ $BOOTSTRAP -eq 1 ]]; then
    verify_bootstrap \
      || { echo "==> Bootstrap verification FAILED — rolling back" >&2
           docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" logs --tail=20 lucia-agenthost-voice 2>&1 || true
           _rollback_live; exit 1; }
    trap - INT TERM HUP    # clear trap — verified waiting state
    rm -f "$ROLLBACK_FILE"
    echo ""
    echo "=== BOOTSTRAP REQUIRED ==="
    echo "AgentHost is running and waiting for Home Assistant configuration."
    echo "Next steps:"
    echo "  1. Open the setup wizard: http://<jetson-ip>:7233/"
    echo "  2. Configure Home Assistant Base URL and Access Token."
    echo "  3. Re-run normal deploy (without --bootstrap) using the same image after setup:"
    echo "     ./deploy-jetson.sh --image $IMAGE_NAME"
    echo ""
    exit 0
  fi

  verify_new \
    || { echo "==> Verification FAILED — rolling back" >&2
         docker compose -p "$DEPLOY_PROJECT" -f "$VOICE_COMPOSE" logs --tail=20 lucia-agenthost-voice 2>&1 || true
         _rollback_live; exit 1; }

  trap - INT TERM HUP    # clear trap — deployment complete

  # Rollback record is intentionally kept so `--rollback` remains usable.
  echo ""
  echo "=== Voice AgentHost deployed ==="
  echo "Dashboard / health : http://<jetson-ip>:7233/  (/health)"
  echo "Wyoming (TCP)      : <jetson-ip>:10400"
  echo "Prior image (for --rollback): ${prior_image:-<none — fresh deploy>}"
  exit 0
}

# ---------------------------------------------------------------------------
# Dispatch only when executed directly. When this file is sourced (regression
# tests invoke the real _npgsql_quote / _ensure_databases / rollback handlers),
# expose the function definitions without running a deployment.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  if [[ $ROLLBACK -eq 1 ]]; then
    do_rollback
  else
    do_deploy
  fi
fi