#!/usr/bin/env bash
# test-deploy-jetson-validation.sh -- Production-coupled regression checks for
# deploy-jetson.sh + docker-compose.jetson-voice.yml.
#
# Every behavioral check INVOKES the real deploy-jetson.sh (as a subprocess, or by
# sourcing it) through controlled command stubs and a real .NET Npgsql parse. The
# tests deliberately do NOT re-implement _npgsql_quote, the image-ref regex,
# _ensure_databases, rollback, ordering, or the signal handler -- so a regression in
# production makes a test FAIL instead of a copied oracle silently passing.
#
# No test framework. No project containers/networks/volumes are created; a
# before/after docker inventory guards against leaks. All scratch lives under a temp
# dir removed on exit. Nothing is written to /tmp.
#
# Run: bash infra/docker/test-deploy-jetson-validation.sh

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_SCRIPT="$SCRIPT_DIR/deploy-jetson.sh"
VOICE_COMPOSE="$SCRIPT_DIR/docker-compose.jetson-voice.yml"

PASS=0; FAIL=0
pass() { echo "PASS: $1"; PASS=$((PASS+1)); }
fail() { echo "FAIL: $1"; FAIL=$((FAIL+1)); }
skip() { echo "SKIP: $1"; }
check() { # label actual expected
  if [[ "$2" == "$3" ]]; then pass "$1"; else fail "$1 (got='$2' expected='$3')"; fi
}

# Immutable image-ref fixtures.
HEX64="166a5b1086fafa89c62481618befde8960d58b391d5a44d2134547c344747978"
VALID_ID="sha256:${HEX64}"
VALID_DIGEST="lucia-agenthost-voice@sha256:${HEX64}"
PRIOR_ID="sha256:$(printf 'a%.0s' {1..64})"

# --- scratch (repo-local, never /tmp) ---
WORK="$(mktemp -d "${SCRIPT_DIR}/.deploytest.XXXXXX")"
cleanup() { rm -rf "$WORK" 2>/dev/null || true; }
trap cleanup EXIT
STUBBIN="$WORK/bin"; mkdir -p "$STUBBIN"

# --- capabilities ---
HAVE_DOCKER=0; command -v docker >/dev/null 2>&1 && HAVE_DOCKER=1
REAL_DOCKER=""; [[ $HAVE_DOCKER -eq 1 ]] && REAL_DOCKER="$(command -v docker)"
HAVE_DOTNET=0; command -v dotnet >/dev/null 2>&1 && HAVE_DOTNET=1

# --- docker inventory snapshot (leak guard) ---
inv() { # c|v|n
  [[ $HAVE_DOCKER -eq 1 ]] || return 0
  case "$1" in
    c) docker ps -aq       2>/dev/null | sort ;;
    v) docker volume ls -q 2>/dev/null | sort ;;
    n) docker network ls -q 2>/dev/null | sort ;;
  esac
}
INV_C0="$(inv c)"; INV_V0="$(inv v)"; INV_N0="$(inv n)"

# ---------------------------------------------------------------------------
# Command stubs. These record what the REAL deploy script asks docker/curl to
# do; they never touch a real daemon (except an opt-in `compose config`
# passthrough for compose rendering).
# ---------------------------------------------------------------------------
cat > "$STUBBIN/docker" <<'STUB'
#!/usr/bin/env bash
set -u
argv="$*"
trace() { [[ -n "${STUB_TRACE:-}" ]] && printf '%s\n' "$1" >> "$STUB_TRACE"; return 0; }
case "${1:-}" in
  image)
    if [[ "$argv" == *"Os"* || "$argv" == *"Architecture"* ]]; then
        printf '%s\n' "${STUB_ARCH-linux/arm64}"
        exit 0
    fi
    exit 0 ;;
  info)  printf '{"nvidia":{"path":"/usr/bin/nvidia-container-runtime"}}\n'; exit 0 ;;
  inspect)
    if   [[ "$argv" == *".Image"*       ]]; then printf '%s\n' "${STUB_PRIOR_IMAGE:-}"
    elif [[ "$argv" == *"State.Health"* ]]; then printf 'healthy\n'; fi
    exit 0 ;;
  exec)
    if [[ "$argv" == *psql* && "$argv" == *pg_database* ]]; then
      db="$(printf '%s' "$argv" | sed -n "s/.*datname='\([A-Za-z0-9_]*\)'.*/\1/p")"
      case " ${STUB_EXISTING_DBS:-} " in *" $db "*) printf ' 1\n' ;; *) : ;; esac
      exit 0
    fi
    if [[ "$argv" == *createdb* ]]; then trace "CREATEDB ${argv##* }"; exit 0; fi
    exit 0 ;;
  compose)
    if [[ "$argv" == *" config"* ]]; then
      [[ -n "${REAL_DOCKER:-}" ]] && exec "$REAL_DOCKER" "$@"
      exit 0
    fi
    if [[ "$argv" == *"up "* && "$argv" == *lucia-postgres* ]]; then trace DATAUP; exit 0; fi
    if [[ "$argv" == *"ps -q lucia-agenthost-voice"* ]]; then
      if [[ "${STUB_CS_STRICT:-0}" == "1" && -z "${CS_LUCIACONFIG:-}" ]]; then
        echo 'stub-compose: required variable CS_LUCIACONFIG is missing or empty' >&2
        trace PS_AGENT_CS_UNSET; exit 1
      fi
      trace PS_AGENT_CS_OK
      # -q: running containers (includes newly-started by current deploy)
      { [[ -n "${STUB_PRIOR_IMAGE:-}" ]] || [[ "${STUB_AGENTHOST_RUNNING:-0}" == "1" ]]; } && printf 'agent-cid\n'
      exit 0
    fi
    [[ "$argv" == *"ps -q lucia-postgres"* ]] && { printf 'pg-cid\n'; exit 0; }
    [[ "$argv" == *"ps -q lucia-redis"*    ]] && { printf 'rd-cid\n'; exit 0; }
    [[ "$argv" == *logs* ]] && exit 0
    if [[ "$argv" == *"up "* && "$argv" == *lucia-agenthost-voice* ]]; then
      upn=1
      if [[ -n "${STUB_STATE:-}" ]]; then
        cf="$STUB_STATE/agent_up_count"
        upn=$(( $(cat "$cf" 2>/dev/null || echo 0) + 1 ))
        printf '%s' "$upn" > "$cf"
      fi
      trace "AGENTUP n=$upn LUCIA_IMAGE=${LUCIA_IMAGE:-}"
      if [[ -n "${STUB_AGENT_UP_SLEEP:-}" && "$upn" == "1" ]]; then
        [[ -n "${STUB_STATE:-}" ]] && : > "$STUB_STATE/agent_up_started"
        sleep "${STUB_AGENT_UP_SLEEP}"
      fi
      if [[ -n "${STUB_AGENT_UP_FAIL_ON:-}" && "$upn" == "${STUB_AGENT_UP_FAIL_ON}" ]]; then
        echo "stub-compose: agenthost up #$upn forced failure" >&2; exit 1
      fi
      exit 0
    fi
    if [[ "$argv" == *stop* && "$argv" == *lucia-agenthost-voice* ]]; then trace AGENTHOST_STOP; exit 0; fi
    if [[ "$argv" == *" rm "* && "$argv" == *lucia-agenthost-voice* ]]; then trace AGENTHOST_RM; exit 0; fi
    { [[ "$argv" == *stop* ]] || [[ "$argv" == *" rm "* ]]; } && exit 0
    exit 0 ;;
  ps)
    # docker ps -aq --filter label= — bootstrap pre-deploy container guard
    if [[ "$argv" == *"-aq"* && "$argv" == *"label=com.docker.compose"* ]]; then
      trace "BOOTSTRAP_GUARD_CHECK"
      { [[ -n "${STUB_PRIOR_IMAGE:-}" ]] || [[ "${STUB_AGENTHOST_STOPPED:-0}" == "1" ]]; } && printf 'agent-cid\n'
      exit 0
    fi
    exit 0 ;;
  logs) [[ -n "${STUB_AGENTHOST_LOGS:-}" ]] && printf '%s\n' "${STUB_AGENTHOST_LOGS}"; exit 0 ;;
  *) exit 0 ;;
esac
STUB
chmod +x "$STUBBIN/docker"

cat > "$STUBBIN/curl" <<'STUB'
#!/usr/bin/env bash
url=""
for a in "$@"; do case "$a" in http://*|https://*) url="$a";; esac; done
case "$url" in
  */health) [[ "${STUB_HEALTH_FAIL:-0}" == "1" ]] && exit 1 || exit 0 ;;
  */api/wyoming/status)
    if [[ -n "${STUB_WYOMING_BODY:-}" ]]; then
      printf '%s' "${STUB_WYOMING_BODY}"
    elif [[ "${STUB_ACCEL:-1}" == "1" ]]; then
      printf '%s' '{"stt":{"ready":true,"activeModel":"sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8","engine":"Hybrid (Offline Re-transcription)"},"vad":{"ready":true,"activeModel":"silero_vad_v5"},"wakeWord":{"ready":true,"activeModel":"sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"},"diarization":{"ready":true,"activeModel":"3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k"},"speechEnhancement":{"ready":true,"activeModel":"gtcrn_simple"},"customWakeWords":{"ready":false},"onnxProvider":{"selected":"CUDAExecutionProvider","sherpaProvider":"cuda","isAccelerated":true,"available":["CUDAExecutionProvider","CPUExecutionProvider"]},"configured":true}'
    else
      printf '%s' '{"stt":{"ready":true,"activeModel":"sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8","engine":"Hybrid (Offline Re-transcription)"},"vad":{"ready":true,"activeModel":"silero_vad_v5"},"wakeWord":{"ready":true,"activeModel":"sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"},"diarization":{"ready":true,"activeModel":"3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k"},"speechEnhancement":{"ready":true,"activeModel":"gtcrn_simple"},"customWakeWords":{"ready":false},"onnxProvider":{"selected":"CPUExecutionProvider","sherpaProvider":"cpu","isAccelerated":false,"available":["CPUExecutionProvider"]},"configured":true}'
    fi
    exit 0 ;;
  *) exit 1 ;;
esac
STUB
chmod +x "$STUBBIN/curl"

cat > "$STUBBIN/wget" <<'STUB'
#!/usr/bin/env bash
url=""
for a in "$@"; do case "$a" in http://*|https://*) url="$a";; esac; done
case "$url" in
  */health) [[ "${STUB_HEALTH_FAIL:-0}" == "1" ]] && exit 1 || exit 0 ;;
  */api/wyoming/status)
    if [[ -n "${STUB_WYOMING_BODY:-}" ]]; then
      printf '%s' "${STUB_WYOMING_BODY}"
    elif [[ "${STUB_ACCEL:-1}" == "1" ]]; then
      printf '%s' '{"stt":{"ready":true},"onnxProvider":{"selected":"CUDAExecutionProvider","sherpaProvider":"cuda","isAccelerated":true,"available":["CUDAExecutionProvider","CPUExecutionProvider"]},"configured":true}'
    else
      printf '%s' '{"stt":{"ready":true},"onnxProvider":{"selected":"CPUExecutionProvider","sherpaProvider":"cpu","isAccelerated":false,"available":["CPUExecutionProvider"]},"configured":true}'
    fi
    exit 0 ;;
  *) exit 1 ;;
esac
STUB
chmod +x "$STUBBIN/wget"

cat > "$STUBBIN/uname" <<'STUB'
#!/usr/bin/env bash
[[ "${1:-}" == "-m" ]] && { echo aarch64; exit 0; }
echo Linux
STUB
chmod +x "$STUBBIN/uname"

cat > "$STUBBIN/python3" <<'STUB'
#!/usr/bin/env bash
echo "ERROR: python3 must not be called by deploy-jetson.sh" >&2
exit 1
STUB
chmod +x "$STUBBIN/python3"

# ---------------------------------------------------------------------------
# Helpers that reuse the REAL production code.
# ---------------------------------------------------------------------------

# Source the real deploy script (guard suppresses dispatch; args cleared) and
# invoke one of its functions with the stub PATH in front.
run_prod_fn() { # fn [args...]
  local fn="$1"; shift
  PATH="$STUBBIN:$PATH" bash -c '
    _script="$1"; _fn="$2"; shift 2
    _args=( "$@" )
    set --
    # shellcheck source=/dev/null
    source "$_script"
    "$_fn" "${_args[@]}"
  ' _ "$DEPLOY_SCRIPT" "$fn" "$@"
}

# Copy the real deploy script + compose into a fresh isolated dir so its
# ROLLBACK_FILE / SCRIPT_DIR are sandboxed per scenario.
prep_rundir() {
  local d; d="$(mktemp -d "$WORK/run.XXXXXX")"
  cp "$DEPLOY_SCRIPT" "$d/deploy-jetson.sh"
  cp "$VOICE_COMPOSE" "$d/docker-compose.jetson-voice.yml"
  printf '%s' "$d"
}

# ---------------------------------------------------------------------------
echo "== Image-ref immutable forms (exercises real do_deploy regex) =="
img_rc() { # ref -> real script dry-run exit code
  PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD=pw \
    bash "$DEPLOY_SCRIPT" --image "$1" --dry-run >/dev/null 2>&1
  echo $?
}
check "accepts registry digest (name@sha256:64hex)"  "$(img_rc "$VALID_DIGEST")" 0
check "accepts local image id (sha256:64hex)"         "$(img_rc "$VALID_ID")"     0
check "rejects mutable tag"        "$([[ $(img_rc 'lucia:r36-poc') -ne 0 ]] && echo reject)"        reject
check "rejects abbreviated id"     "$([[ $(img_rc 'sha256:166a5b1086fa') -ne 0 ]] && echo reject)" reject
check "rejects 63-hex (one short)" "$([[ $(img_rc "sha256:${HEX64%?}") -ne 0 ]] && echo reject)"   reject
check "rejects bare latest"        "$([[ $(img_rc 'latest') -ne 0 ]] && echo reject)"              reject

# ---------------------------------------------------------------------------
echo "== Architecture check (linux/arm64 required; AMD64 + unknown rejected) =="
arch_rc() { # arch [ref]
  local ref="${2:-$VALID_ID}"
  STUB_ARCH="$1" PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD=pw \
    bash "$DEPLOY_SCRIPT" --image "$ref" --dry-run >/dev/null 2>&1
  echo $?
}
check "accepts linux/arm64 (local image-id form)"  "$(arch_rc linux/arm64)"                  0
check "accepts linux/arm64 (registry digest form)" "$(arch_rc linux/arm64 "$VALID_DIGEST")"  0
check "rejects linux/amd64"  "$([[ $(arch_rc linux/amd64)  -ne 0 ]] && echo reject)" reject
check "rejects unknown arch" "$([[ $(arch_rc linux/x86_64) -ne 0 ]] && echo reject)" reject

# Mutation: removing the gate must let AMD64 through, proving it is the effective blocker.
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
sed -i '/Refusing non-ARM64/d' "$mut_deploy"
mut_rc=0
STUB_ARCH=linux/amd64 PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD=pw \
  bash "$mut_deploy" --image "$VALID_ID" --dry-run >/dev/null 2>&1 || mut_rc=$?
[[ "$mut_rc" -eq 0 ]] \
  && pass "mutation: arch gate removal lets amd64 through (gate is effective blocker)" \
  || fail "mutation: amd64 still blocked in mutant — gate deletion insufficient"

# ---------------------------------------------------------------------------
echo "== Compose renders both immutable image forms (real docker compose config) =="
if [[ $HAVE_DOCKER -eq 1 ]]; then
  render_ok() { # ref label
    local out
    out="$(PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD=pw \
      bash "$DEPLOY_SCRIPT" --image "$1" --dry-run 2>&1 || true)"
    if printf '%s' "$out" | grep -qF "sha256:${HEX64}"; then
      pass "compose config renders $2"
    else
      fail "compose config did not render $2"
    fi
  }
  render_ok "$VALID_DIGEST" "registry digest form"
  render_ok "$VALID_ID"     "local image-id form"
else
  skip "compose render check (docker unavailable)"
fi

# ---------------------------------------------------------------------------
echo "== Secret non-disclosure (real dry-run output) =="
SENTINEL="RIPLEY_SENTINEL_$$_$RANDOM"
DRY_OUT="$(PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD="$SENTINEL" \
  bash "$DEPLOY_SCRIPT" --image "$VALID_ID" --dry-run 2>&1 || true)"
if printf '%s' "$DRY_OUT" | grep -qF "$SENTINEL"; then
  fail "dry-run leaked the real password into output"
else
  pass "dry-run never prints the real password (CS_* + POSTGRES_PASSWORD redacted)"
fi

# ---------------------------------------------------------------------------
echo "== _npgsql_quote output parses in a real Npgsql builder (';' + single quote) =="
if [[ $HAVE_DOTNET -eq 1 ]]; then
  RAWPW="p;'q"
  QUOTED="$(run_prod_fn _npgsql_quote "$RAWPW")"
  CSVAL="Host=lucia-postgres;Port=5432;Username=postgres;Password=${QUOTED};Database=luciatasks"
  cat > "$WORK/parse.cs" <<'CS'
#:package Npgsql
using Npgsql;
var cs = Environment.GetEnvironmentVariable("CSVAL") ?? "";
var expect = Environment.GetEnvironmentVariable("EXPECT") ?? "";
try
{
    var b = new NpgsqlConnectionStringBuilder(cs);
    bool ok = b.Password == expect && b.Database == "luciatasks" && b.Host == "lucia-postgres";
    Console.WriteLine(ok
        ? "NPGSQL_PARSE=OK"
        : $"NPGSQL_PARSE=MISMATCH host=[{b.Host}] db=[{b.Database}] pwMatch={b.Password == expect}");
    if (!ok) Environment.Exit(4);
}
catch (Exception e)
{
    Console.WriteLine("NPGSQL_PARSE=ERROR " + e.GetType().Name);
    Environment.Exit(3);
}
CS
  PARSE_OUT="$(CSVAL="$CSVAL" EXPECT="$RAWPW" dotnet run "$WORK/parse.cs" 2>&1 || true)"
  if printf '%s' "$PARSE_OUT" | grep -q 'NPGSQL_PARSE=OK'; then
    pass "real quoting keeps ';' and quote inside the password; DB uncorrupted"
  else
    fail "Npgsql parse failed: $(printf '%s' "$PARSE_OUT" | grep -m1 'NPGSQL_PARSE=' || echo "$PARSE_OUT" | tail -n1)"
  fi
else
  fail "Npgsql parse check requires dotnet — install dotnet SDK to validate production quoting"
fi

# ---------------------------------------------------------------------------
echo "== _ensure_databases creates only missing DBs on a reused volume (real function) =="
ENS_TRACE="$WORK/ens_trace"; : > "$ENS_TRACE"
( export STUB_TRACE="$ENS_TRACE" STUB_EXISTING_DBS="luciaconfig"
  run_prod_fn _ensure_databases ) >/dev/null 2>&1 || true
created="$(grep '^CREATEDB ' "$ENS_TRACE" 2>/dev/null | awk '{print $2}' | sort | tr '\n' ' ')"
check "creates exactly the missing DBs (luciaconfig pre-exists)" "$created" "luciatasks luciatraces "

# ---------------------------------------------------------------------------
echo "== Deploy captures prior .Image AFTER CS vars, keeps rollback record on success =="
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" STUB_PRIOR_IMAGE="$PRIOR_ID" \
         STUB_CS_STRICT=1 STUB_ACCEL=1 REAL_DOCKER="$REAL_DOCKER" \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" ) >"$d/out" 2>&1
rc=$?
rec="$(cat "$d/.rollback-image" 2>/dev/null || true)"
check "deploy succeeds (exit 0)" "$rc" 0
check "prior .Image captured (CS vars existed at compose-ps time)" "$rec" "$PRIOR_ID"
if grep -q '^PS_AGENT_CS_OK$' "$d/trace" 2>/dev/null; then
  pass "prior-image compose ps saw CS_LUCIACONFIG set (Blocker 1 ordering fixed)"
else
  fail "prior-image compose ps ran before CS vars (Blocker 1 regression)"
fi
[[ -f "$d/.rollback-image" ]] \
  && pass "rollback record preserved after successful deploy (usable by --rollback)" \
  || fail "rollback record deleted after successful deploy"

# ---------------------------------------------------------------------------
echo "== Failed verification rolls back to the prior image and clears the record =="
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" STUB_PRIOR_IMAGE="$PRIOR_ID" \
         STUB_CS_STRICT=1 STUB_ACCEL=0 POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" ) >"$d/out" 2>&1
rc=$?
check "deploy reports failure (exit 1) when CUDA EP is absent" "$rc" 1
if grep -q "^AGENTUP n=2 LUCIA_IMAGE=${PRIOR_ID}\$" "$d/trace" 2>/dev/null; then
  pass "rollback re-deployed the prior image (AGENTUP #2 == prior)"
else
  fail "rollback did not re-deploy prior image: $(grep '^AGENTUP' "$d/trace" 2>/dev/null | tr '\n' '|')"
fi
[[ ! -f "$d/.rollback-image" ]] \
  && pass "rollback record cleared after a successful restore" \
  || fail "rollback record not cleared after successful restore"

# ---------------------------------------------------------------------------
echo "== A failed rollback preserves the record as evidence =="
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" STUB_PRIOR_IMAGE="$PRIOR_ID" \
         STUB_CS_STRICT=1 STUB_ACCEL=0 STUB_AGENT_UP_FAIL_ON=2 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" ) >"$d/out" 2>&1
rc=$?
check "deploy reports failure (exit 1)" "$rc" 1
[[ -f "$d/.rollback-image" ]] \
  && pass "rollback record preserved when restore fails (evidence retained)" \
  || fail "rollback record deleted despite a failed restore"
grep -q 'evidence preserved' "$d/out" 2>/dev/null \
  && pass "operator sees 'evidence preserved' guidance" \
  || fail "no 'evidence preserved' message emitted on failed rollback"

# ---------------------------------------------------------------------------
echo "== SIGTERM during deploy triggers exactly one rollback and exits 130 =="
d="$(prep_rundir)"
STUB_TRACE="$d/trace" STUB_STATE="$d" STUB_PRIOR_IMAGE="$PRIOR_ID" \
  STUB_CS_STRICT=1 STUB_ACCEL=1 STUB_AGENT_UP_SLEEP=4 \
  POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH" \
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" >"$d/out" 2>&1 &
dpid=$!
for _ in $(seq 1 100); do [[ -f "$d/agent_up_started" ]] && break; sleep 0.1; done
kill -TERM "$dpid" 2>/dev/null || true
sleep 0.2
kill -TERM "$dpid" 2>/dev/null || true    # second signal must be ignored (re-entry guard)
wait "$dpid"; rc=$?
check "deploy exits 130 on signal" "$rc" 130
upcount="$(cat "$d/agent_up_count" 2>/dev/null || echo 0)"
check "exactly one rollback (initial up + one rollback up == 2)" "$upcount" 2

# ---------------------------------------------------------------------------
echo "== CUDA EP status parser (verify_new — real function, controlled stub) =="
# Invoke real verify_new with a controlled wyoming response body.
# Save the script path before set -- clears positional params (same pattern as run_prod_fn).
cuda_verify() { # body -> 0 if verify_new passes, non-zero if it fails
  STUB_WYOMING_BODY="$1" PATH="$STUBBIN:$PATH" bash -c \
    '_s="$1"; set --; source "$_s"; verify_new' _ "$DEPLOY_SCRIPT" >/dev/null 2>&1
}

# --- Production-coupled realistic nested DTO. Mirrors WyomingStatusApi.cs GetWyomingStatus:
#     System.Text.Json web defaults => camelCase; each component (stt/vad/wakeWord/
#     diarization/speechEnhancement/customWakeWords/onnxProvider) is its own nested object and
#     onnxProvider carries an available[] array. This nested shape is exactly what the old
#     flat two-level envelope rejected. ---
REAL_HEAD='"stt":{"ready":true,"activeModel":"sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8","engine":"Hybrid (Offline Re-transcription)"},"vad":{"ready":true,"activeModel":"silero_vad_v5"},"wakeWord":{"ready":true,"activeModel":"sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"},"diarization":{"ready":true,"activeModel":"3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k"},"speechEnhancement":{"ready":true,"activeModel":"gtcrn_simple"},"customWakeWords":{"ready":false}'
REAL_ONNX='"onnxProvider":{"selected":"CUDAExecutionProvider","sherpaProvider":"cuda","isAccelerated":true,"available":["CUDAExecutionProvider","CPUExecutionProvider"]}'
REAL_STATUS="{${REAL_HEAD},${REAL_ONNX},\"configured\":true}"

cuda_verify "$REAL_STATUS" \
  && pass "realistic nested DTO (compact): accepted" \
  || fail "realistic nested DTO (compact): rejected (must accept — this nested shape was the blocker)"

REAL_SPACED='{ "stt": { "ready": true, "activeModel": null }, "onnxProvider": { "selected": "CUDAExecutionProvider", "sherpaProvider": "cuda", "isAccelerated": true, "available": ["CUDAExecutionProvider", "CPUExecutionProvider"] }, "configured": true }'
cuda_verify "$REAL_SPACED" \
  && pass "realistic nested DTO (spaced): accepted" \
  || fail "realistic nested DTO (spaced): rejected (must accept)"

REAL_REV='{"stt":{"ready":true,"activeModel":null},"onnxProvider":{"isAccelerated":true,"available":["CUDAExecutionProvider"],"sherpaProvider":"cuda","selected":"CUDAExecutionProvider"},"configured":true}'
cuda_verify "$REAL_REV" \
  && pass "realistic nested DTO (onnxProvider key order reversed): accepted" \
  || fail "realistic nested DTO (reversed key order): rejected (must accept)"

REAL_LF=$'{\n  "stt": {"ready": true, "activeModel": null},\n  "onnxProvider": {\n    "selected": "CUDAExecutionProvider",\n    "sherpaProvider": "cuda",\n    "isAccelerated": true,\n    "available": ["CUDAExecutionProvider", "CPUExecutionProvider"]\n  },\n  "configured": true\n}'
cuda_verify "$REAL_LF" \
  && pass "realistic nested DTO (LF/indented multi-line): accepted" \
  || fail "realistic nested DTO (LF multi-line): rejected (must accept)"

REAL_CRLF=$'{\r\n  "onnxProvider": {\r\n    "selected": "CUDAExecutionProvider",\r\n    "isAccelerated": true\r\n  },\r\n  "configured": true\r\n}'
cuda_verify "$REAL_CRLF" \
  && pass "realistic nested DTO (CRLF multi-line): accepted" \
  || fail "realistic nested DTO (CRLF multi-line): rejected (must accept)"

# Real nested shape but the accelerated provider is CPU while CUDA only appears in
# available[] — must reject (selected is read from onnxProvider, available[] is not selected).
cuda_verify "{${REAL_HEAD},\"onnxProvider\":{\"selected\":\"CPUExecutionProvider\",\"sherpaProvider\":\"cpu\",\"isAccelerated\":true,\"available\":[\"CUDAExecutionProvider\",\"CPUExecutionProvider\"]},\"configured\":true}" \
  && fail "realistic nested: CPU selected + CUDA in available[] — must reject" \
  || pass "realistic nested: CPU selected + CUDA in available[] correctly rejected"

# Same-object guarantee: selected + isAccelerated must both come from the SAME onnxProvider
# object. A CUDA "selected" in a sibling object must never pair with onnxProvider's
# acceleration, and onnxProvider's own selected must never pair with a sibling's acceleration.
cuda_verify '{"onnxProvider":{"selected":"CPUExecutionProvider","isAccelerated":true},"sib":{"selected":"CUDAExecutionProvider","isAccelerated":false}}' \
  && fail "cross-object: sibling CUDA-selected paired with onnxProvider accel — must reject" \
  || pass "cross-object: sibling CUDA-selected cannot pair with onnxProvider acceleration (rejected)"
cuda_verify '{"sib":{"selected":"CUDAExecutionProvider","isAccelerated":true},"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":false}}' \
  && fail "cross-object: onnxProvider CUDA-selected paired with sibling accel — must reject" \
  || pass "cross-object: onnxProvider isAccelerated:false is read from its own object (rejected)"

cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":true}}' \
  && pass "compact JSON: CUDA selected + isAccelerated:true accepted" \
  || fail "compact JSON: CUDA selected + isAccelerated:true rejected (must accept)"
cuda_verify '{"onnxProvider": {"selected": "CUDAExecutionProvider", "isAccelerated": true}}' \
  && pass "spaced JSON: spaces around colons/values accepted" \
  || fail "spaced JSON: whitespace variant rejected (must accept)"
cuda_verify '{"onnxProvider":{"isAccelerated":true,"selected":"CUDAExecutionProvider"}}' \
  && pass "reversed key order: isAccelerated before selected accepted" \
  || fail "reversed key order rejected (must accept)"
# Adversarial: CUDA appears only in available[], selected is CPU, isAccelerated:true —
# old substring check would falsely accept this; fixed check must reject it.
cuda_verify '{"onnxProvider":{"selected":"CPUExecutionProvider","available":["CUDAExecutionProvider"],"isAccelerated":true}}' \
  && fail "CPU selected with CUDA in available — must reject" \
  || pass "CPU selected with CUDA in available correctly rejected"
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":false}}' \
  && fail "CUDA selected but isAccelerated:false — must reject" \
  || pass "CUDA selected isAccelerated:false correctly rejected"
cuda_verify '{"onnxProvider":{"isAccelerated":true}}' \
  && fail "missing selected key — must reject" \
  || pass "missing selected key correctly rejected"
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider"}}' \
  && fail "missing isAccelerated key — must reject" \
  || pass "missing isAccelerated key correctly rejected"
cuda_verify '{}' \
  && fail "empty JSON object — must reject" \
  || pass "empty JSON object correctly rejected"
cuda_verify 'not-json' \
  && fail "malformed status — must reject" \
  || pass "malformed status correctly rejected"

# Additional valid: tabs and LF/CR around separators (old tr-d ' \t' failed on LF/CR).
cuda_verify $'{"onnxProvider":{"selected":\t"CUDAExecutionProvider","isAccelerated":\ttrue\t}}' \
  && pass "tab after colon/value accepted" \
  || fail "tab after colon/value rejected (must accept)"
cuda_verify $'{"onnxProvider":{"selected":\n"CUDAExecutionProvider","isAccelerated":\ntrue\n}}' \
  && pass "LF after colon accepted" \
  || fail "LF after colon rejected (must accept)"
cuda_verify $'{"onnxProvider":{"selected":\r\n"CUDAExecutionProvider","isAccelerated":\r\ntrue\r\n}}' \
  && pass "CRLF after colon accepted" \
  || fail "CRLF after colon rejected (must accept)"

# Additional invalid: cases the old tr-d approach falsely accepted.
cuda_verify '{"onnxProvider":{"selected":"CUDA ExecutionProvider","isAccelerated":true}}' \
  && fail "altered provider 'CUDA ExecutionProvider' — must reject" \
  || pass "altered provider 'CUDA ExecutionProvider' correctly rejected"
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":"true"}}' \
  && fail "quoted boolean \"true\" — must reject" \
  || pass "quoted boolean \"true\" correctly rejected"
cuda_verify $'{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":t r u e}}' \
  && fail "spaced boolean t r u e — must reject" \
  || pass "spaced boolean t r u e correctly rejected"
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":truefalse}}' \
  && fail "truefalse — must reject (missing token boundary)" \
  || pass "truefalse correctly rejected (token boundary enforced)"

# JSON envelope and content validation (these were accepted by old regex).
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":true}garbage}' \
  && fail "trailing garbage after inner close } — must reject (envelope check)" \
  || pass "trailing garbage after inner close } correctly rejected"
cuda_verify '{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":true,garbage}}' \
  && fail "bare-word garbage after true, inside inner object — must reject" \
  || pass "bare-word garbage after true, inside inner object correctly rejected"

# Mutation test: weakening the selected check to a bare CUDAExecutionProvider substring
# must let the CPU-selected-CUDA-available payload through, proving the selected check
# is the effective gate against available-array false positives.
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
sed -i 's/local _sel=.*/local _sel='"'"'"CUDAExecutionProvider"'"'"'/' "$mut_deploy"
mut_slipped=0
STUB_WYOMING_BODY='{"onnxProvider":{"selected":"CPUExecutionProvider","available":["CUDAExecutionProvider"],"isAccelerated":true}}' \
  PATH="$STUBBIN:$PATH" bash -c '_s="$1"; set --; source "$_s"; verify_new' _ "$mut_deploy" >/dev/null 2>&1 \
  && mut_slipped=1 || true
[[ "$mut_slipped" -eq 1 ]] \
  && pass "mutation: bare-substring weakening lets CPU-selected-CUDA-available through (selected check is the effective gate)" \
  || fail "mutation: weakened script still rejected CPU-selected — selected check may be missing or wrong"

# Mutation: re-introducing destructive whitespace stripping must falsely accept "CUDA ExecutionProvider".
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
export _MUT_TRLINE='    status_json="$(printf '"'"'%s'"'"' "$status_json" | tr -d '"'"' \t'"'"')"'
awk '/if \[\[ "\$status_json" =~ \$_prov/ { print ENVIRON["_MUT_TRLINE"] } { print }' \
  "$mut_deploy" > "$mut_deploy.tmp" && mv "$mut_deploy.tmp" "$mut_deploy"
unset _MUT_TRLINE
mut_slipped=0
STUB_WYOMING_BODY='{"onnxProvider":{"selected":"CUDA ExecutionProvider","isAccelerated":true}}' \
  PATH="$STUBBIN:$PATH" bash -c '_s="$1"; set --; source "$_s"; verify_new' _ "$mut_deploy" >/dev/null 2>&1 \
  && mut_slipped=1 || true
[[ "$mut_slipped" -eq 1 ]] \
  && pass "mutation: tr-d normalization falsely accepts 'CUDA ExecutionProvider' (raw matching guards string values)" \
  || fail "mutation: tr-d mutant still rejected 'CUDA ExecutionProvider' — raw-match guard may be ineffective"

# Mutation: removing the boolean token boundary [,}] must let truefalse through.
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
sed -i 's/local _acc=.*/local _acc='"'"'"isAccelerated"[[:space:]]*:[[:space:]]*true'"'"'/' "$mut_deploy"
mut_slipped=0
STUB_WYOMING_BODY='{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":truefalse}}' \
  PATH="$STUBBIN:$PATH" bash -c '_s="$1"; set --; source "$_s"; verify_new' _ "$mut_deploy" >/dev/null 2>&1 \
  && mut_slipped=1 || true
[[ "$mut_slipped" -eq 1 ]] \
  && pass "mutation: removing boolean boundary lets truefalse through (boundary is the effective guard)" \
  || fail "mutation: boundary-removed script still rejected truefalse — boundary check may be missing or wrong"

# Mutation: dropping the ',' / '}' boundary that must follow the onnxProvider object lets
# garbage wedged after its closing brace slip in, proving the _prov boundary is the gate
# for post-object garbage (the case the old flat envelope caught via a trailing-}).
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
export _MUT_PROVLINE='    local _prov='\''"onnxProvider"[[:space:]]*:[[:space:]]*(\{[^{}]*\})'\'''
awk '/local _prov=/ { print ENVIRON["_MUT_PROVLINE"]; next } { print }' \
  "$mut_deploy" > "$mut_deploy.tmp" && mv "$mut_deploy.tmp" "$mut_deploy"
unset _MUT_PROVLINE
mut_slipped=0
STUB_WYOMING_BODY='{"onnxProvider":{"selected":"CUDAExecutionProvider","isAccelerated":true}garbage}' \
  PATH="$STUBBIN:$PATH" bash -c '_s="$1"; set --; source "$_s"; verify_new' _ "$mut_deploy" >/dev/null 2>&1 \
  && mut_slipped=1 || true
[[ "$mut_slipped" -eq 1 ]] \
  && pass "mutation: removing onnxProvider trailing boundary lets post-object garbage through (boundary is the effective gate)" \
  || fail "mutation: boundary-removed _prov still rejected post-object garbage — boundary check may be missing or wrong"

# Mutation: matching selected/isAccelerated against the whole body instead of the extracted
# onnxProvider object lets a sibling's CUDA "selected" cross-pair with onnxProvider's
# acceleration, proving the same-object extraction is the effective anti-cross-pairing gate.
mut_dir="$(prep_rundir)"; mut_deploy="$mut_dir/deploy-jetson.sh"
sed -i 's/_prov_obj" =~/status_json" =~/g' "$mut_deploy"
mut_slipped=0
STUB_WYOMING_BODY='{"onnxProvider":{"selected":"CPUExecutionProvider","isAccelerated":true},"sib":{"selected":"CUDAExecutionProvider","isAccelerated":false}}' \
  PATH="$STUBBIN:$PATH" bash -c '_s="$1"; set --; source "$_s"; verify_new' _ "$mut_deploy" >/dev/null 2>&1 \
  && mut_slipped=1 || true
[[ "$mut_slipped" -eq 1 ]] \
  && pass "mutation: whole-body matching lets cross-object CUDA/accel pair through (same-object extraction is the effective gate)" \
  || fail "mutation: whole-body matching still rejected cross-object pairing — same-object extraction may be missing or wrong"


# ---------------------------------------------------------------------------
echo "== Compose static invariants (approved shape must not drift) =="
compose_has() { grep -qE "$1" "$VOICE_COMPOSE"; }
svc_count="$(grep -cE '^  lucia-(postgres|redis|agenthost-voice):' "$VOICE_COMPOSE")"
check "exactly three services (postgres, redis, agenthost)" "$svc_count" 3
compose_has '10400:10400' && pass "Wyoming port 10400 mapped" || fail "Wyoming port 10400 missing"
compose_has 'CS_LUCIACONFIG'  && pass "AgentHost consumes CS_LUCIACONFIG" || fail "CS_LUCIACONFIG not referenced"
grep -q 'container_name' "$VOICE_COMPOSE" && fail "container_name present (breaks project isolation)" \
                                          || pass "no container_name (project-scoped names)"
grep -qiE '(^|\s)build:' "$VOICE_COMPOSE" && fail "build: present (must use immutable image)" \
                                          || pass "no build: stanza (immutable image only)"
grep -qi 'mongo' "$VOICE_COMPOSE" && fail "unexpected mongo service" || pass "no mongo service"

# ---------------------------------------------------------------------------
echo "== No python in deploy-jetson.sh =="
if grep -qF 'python' "$DEPLOY_SCRIPT"; then
  fail "python reference in deploy-jetson.sh (on-device Python dependency not removed)"
else
  pass "no python in deploy-jetson.sh"
fi

# ---------------------------------------------------------------------------
echo "== Bootstrap: verify_bootstrap (real function, controlled stub) =="
# Invoke real verify_bootstrap with controlled /health and docker logs output.
bootstrap_verify() { # logs_content -> 0 if verify_bootstrap passes
  STUB_AGENTHOST_RUNNING=1 STUB_AGENTHOST_LOGS="$1" BOOTSTRAP_LOG_TIMEOUT=3 \
    PATH="$STUBBIN:$PATH" bash -c \
      '_s="$1"; set --; source "$_s"; verify_bootstrap' _ "$DEPLOY_SCRIPT" >/dev/null 2>&1
}

BOOT_MARKER='[AgentInitializationService] Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)'

bootstrap_verify "$BOOT_MARKER" \
  && pass "bootstrap verify: exact HA wait marker accepted" \
  || fail "bootstrap verify: exact HA wait marker rejected (must accept)"

bootstrap_verify 'Waiting for Home Assistant configuration' \
  && fail "bootstrap verify: only first marker — must reject (second missing)" \
  || pass "bootstrap verify: only first marker correctly rejected"

bootstrap_verify 'BaseUrl and AccessToken must be set' \
  && fail "bootstrap verify: only second marker — must reject (first missing)" \
  || pass "bootstrap verify: only second marker correctly rejected"

bootstrap_verify '' \
  && fail "bootstrap verify: empty logs — must reject" \
  || pass "bootstrap verify: empty logs correctly rejected"

bootstrap_verify 'AgentHost started OK. Wyoming ready.' \
  && fail "bootstrap verify: wrong content — must reject" \
  || pass "bootstrap verify: wrong log content correctly rejected"

# Cross-line: message split across two log lines — single-line grep must reject both halves.
bootstrap_verify $'Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set.\nComplete the setup wizard to continue.)' \
  && fail "bootstrap verify: split cross-line message — must reject" \
  || pass "bootstrap verify: split cross-line message correctly rejected"

# Stale fragment: partial message without the closing clause — must reject.
bootstrap_verify 'Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue' \
  && fail "bootstrap verify: truncated stale-fragment — must reject" \
  || pass "bootstrap verify: truncated stale-fragment correctly rejected"

# Large-log SIGPIPE regression: >=400 KB log with exact marker at mid-point (500-line
# prefix, then marker, then 4500-line suffix).  The fix — a direct [[ "$logs" == *marker* ]]
# glob — avoids all pipes so no SIGPIPE under set -o pipefail regardless of marker position.
# Log is written to a file; only LL_LOG_FILE (a short path) is exported so execve's
# env-var ARG_MAX ceiling is not hit (~440 KB would fail with "Argument list too long").
_ll_log="$WORK/large_boot.log"
{
  for _ll_i in $(seq 1 500); do
    printf 'INFO: AgentHost startup log line %d -- routine boot noise (400 KB prefix/suffix large-log regression)\n' "$_ll_i"
  done
  printf '%s\n' '[AgentInitializationService] Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)'
  for _ll_i in $(seq 501 5000); do
    printf 'INFO: AgentHost startup log line %d -- routine boot noise (400 KB prefix/suffix large-log regression)\n' "$_ll_i"
  done
} > "$_ll_log"
_ll_bin="$WORK/llbin"; mkdir -p "$_ll_bin"
for _llf in curl wget uname python3; do [[ -f "$STUBBIN/$_llf" ]] && cp "$STUBBIN/$_llf" "$_ll_bin/$_llf"; done
cat > "$_ll_bin/docker" <<'LLSTUB'
#!/usr/bin/env bash
set -u
argv="$*"
case "${1:-}" in
  compose) [[ "$argv" == *"ps -q lucia-agenthost-voice"* ]] && printf 'agent-cid\n'; exit 0 ;;
  logs) cat "${LL_LOG_FILE}"; exit 0 ;;
  *) exit 0 ;;
esac
LLSTUB
chmod +x "$_ll_bin/docker"
_ll_rc=0
BOOTSTRAP_LOG_TIMEOUT=5 LL_LOG_FILE="$_ll_log" PATH="$_ll_bin:$PATH" bash -c \
  '_s="$1"; set --; source "$_s"; verify_bootstrap' _ "$DEPLOY_SCRIPT" >/dev/null 2>&1 \
  || _ll_rc=$?
[[ "$_ll_rc" -eq 0 ]] \
  && pass "large-log SIGPIPE regression: >=400 KB prefix/suffix log with exact marker accepted" \
  || fail "large-log SIGPIPE regression: FAILED (rc=${_ll_rc}) — glob check may have regressed to a pipe"

# Pipeline regression proof: reintroducing printf|grep-qF under pipefail fails on this
# same content (grep exits after the mid-point match; printf still writing >200 KB suffix
# gets SIGPIPE).  Read from file in this shell — no child-process env-var ARG_MAX limit.
_ll_content="$(cat "$_ll_log")"
_old_pipeline_rc=0
( set -o pipefail
  printf '%s' "$_ll_content" | grep -qF \
    'Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)'
) 2>/dev/null || _old_pipeline_rc=$?
if [[ "$_old_pipeline_rc" -ne 0 ]]; then
  pass "pipeline regression proof: printf|grep-qF exits non-zero (rc=${_old_pipeline_rc}) on >=400 KB mid-match log under pipefail"
else
  skip "pipeline regression proof: no SIGPIPE on this platform — coverage provided by the large-log bootstrap_verify test"
fi

# ---------------------------------------------------------------------------
echo "== Bootstrap: integration (real script, controlled stubs) =="

# Fresh bootstrap success: exit 0, BOOTSTRAP REQUIRED output, no rollback file, AGENTUP once.
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_AGENTHOST_RUNNING=1 \
         STUB_AGENTHOST_LOGS="$BOOT_MARKER" \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap ) >"$d/out" 2>&1
rc=$?
check "bootstrap fresh: exit 0 on verified wait state" "$rc" 0
grep -q 'BOOTSTRAP REQUIRED' "$d/out" \
  && pass "bootstrap fresh: BOOTSTRAP REQUIRED in output" \
  || fail "bootstrap fresh: BOOTSTRAP REQUIRED missing from output"
[[ ! -f "$d/.rollback-image" ]] \
  && pass "bootstrap fresh: rollback record not retained" \
  || fail "bootstrap fresh: rollback record incorrectly retained"
upcount="$(cat "$d/agent_up_count" 2>/dev/null || echo 0)"
check "bootstrap fresh: AgentHost started exactly once (no rollback)" "$upcount" 1
grep -q 'BOOTSTRAP_GUARD_CHECK' "$d/trace" 2>/dev/null \
  && pass "bootstrap fresh: container guard ran (genuine fresh — would fail if guard removed)" \
  || fail "bootstrap fresh: container guard trace missing (guard regression)"

# Normal mode (no --bootstrap) with CPU provider: verify_new fails, rolls back.
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_ACCEL=0 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" ) >"$d/out" 2>&1
rc=$?
check "normal mode without --bootstrap with CPU provider: exit 1 (--bootstrap required)" "$rc" 1

# Bootstrap with prior AgentHost: rejected before mutation (no DATAUP in trace).
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" STUB_PRIOR_IMAGE="$PRIOR_ID" \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap ) >"$d/out" 2>&1
rc=$?
check "bootstrap with prior AgentHost: rejected (exit 1)" "$rc" 1
grep -q 'DATAUP' "$d/trace" 2>/dev/null \
  && fail "bootstrap with prior AgentHost: data services started (mutation occurred)" \
  || pass "bootstrap with prior AgentHost: rejected before mutation (no DATAUP)"

# Bootstrap with stopped AgentHost (ps -aq detects it; ps -q would miss a stopped container).
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_AGENTHOST_STOPPED=1 \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap ) >"$d/out" 2>&1
rc=$?
check "bootstrap with stopped AgentHost: rejected (exit 1)" "$rc" 1
grep -q 'DATAUP' "$d/trace" 2>/dev/null \
  && fail "bootstrap with stopped AgentHost: data services started (mutation occurred)" \
  || pass "bootstrap with stopped AgentHost: rejected before mutation (no DATAUP)"

# Bootstrap with wrong log marker: verify_bootstrap fails, rolls back.
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_AGENTHOST_RUNNING=1 \
         STUB_AGENTHOST_LOGS="AgentHost started OK. Wyoming ready." \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap ) >"$d/out" 2>&1
rc=$?
check "bootstrap with wrong log marker: exit 1 (rollback)" "$rc" 1
grep -q 'AGENTHOST_STOP' "$d/trace" 2>/dev/null \
  && pass "bootstrap wrong log marker: stop issued in fresh rollback" \
  || fail "bootstrap wrong log marker: stop not found in trace"
grep -q 'AGENTHOST_RM' "$d/trace" 2>/dev/null \
  && pass "bootstrap wrong log marker: rm issued in fresh rollback" \
  || fail "bootstrap wrong log marker: rm not found in trace"
[[ ! -f "$d/.rollback-image" ]] \
  && pass "bootstrap wrong log marker: rollback file cleaned up" \
  || fail "bootstrap wrong log marker: rollback file not cleaned up"

# Bootstrap with /health failure: verify_bootstrap fails, rolls back.
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_AGENTHOST_RUNNING=1 \
         STUB_AGENTHOST_LOGS="$BOOT_MARKER" \
         STUB_HEALTH_FAIL=1 \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap --health-timeout 5 ) >"$d/out" 2>&1
rc=$?
check "bootstrap with /health failure: exit 1 (rollback)" "$rc" 1
grep -q 'AGENTHOST_STOP' "$d/trace" 2>/dev/null \
  && pass "bootstrap health failure: stop issued in fresh rollback" \
  || fail "bootstrap health failure: stop not found in trace"
grep -q 'AGENTHOST_RM' "$d/trace" 2>/dev/null \
  && pass "bootstrap health failure: rm issued in fresh rollback" \
  || fail "bootstrap health failure: rm not found in trace"
[[ ! -f "$d/.rollback-image" ]] \
  && pass "bootstrap health failure: rollback file cleaned up" \
  || fail "bootstrap health failure: rollback file not cleaned up"

# Bootstrap compose-up crash: error path triggers rollback before verify_bootstrap.
d="$(prep_rundir)"
( export STUB_TRACE="$d/trace" STUB_STATE="$d" \
         STUB_AGENTHOST_LOGS="$BOOT_MARKER" \
         BOOTSTRAP_LOG_TIMEOUT=3 \
         STUB_AGENT_UP_FAIL_ON=1 \
         POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH"
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap ) >"$d/out" 2>&1
rc=$?
check "bootstrap compose-up crash: exit 1 (rollback)" "$rc" 1
grep -q 'AGENTHOST_STOP' "$d/trace" 2>/dev/null \
  && pass "bootstrap compose-up crash: stop issued in fresh rollback" \
  || fail "bootstrap compose-up crash: stop not found in trace"
grep -q 'AGENTHOST_RM' "$d/trace" 2>/dev/null \
  && pass "bootstrap compose-up crash: rm issued in fresh rollback" \
  || fail "bootstrap compose-up crash: rm not found in trace"
[[ ! -f "$d/.rollback-image" ]] \
  && pass "bootstrap compose-up crash: rollback file cleaned up" \
  || fail "bootstrap compose-up crash: rollback file not cleaned up"

# --dry-run --bootstrap: exit 0, mentions BOOTSTRAP in output.
DRY_BOOT_RC=0
DRY_BOOT_OUT="$(PATH="$STUBBIN:$PATH" REAL_DOCKER="$REAL_DOCKER" POSTGRES_PASSWORD=pw \
  bash "$DEPLOY_SCRIPT" --image "$VALID_ID" --dry-run --bootstrap 2>&1)" || DRY_BOOT_RC=$?
check "--dry-run --bootstrap: exit 0" "$DRY_BOOT_RC" 0
printf '%s' "$DRY_BOOT_OUT" | grep -qi 'BOOTSTRAP' \
  && pass "--dry-run --bootstrap: BOOTSTRAP mentioned in output" \
  || fail "--dry-run --bootstrap: BOOTSTRAP not mentioned in output"

# Bootstrap SIGTERM before verify_bootstrap: trap fires, rollback, exit 130.
d="$(prep_rundir)"
STUB_TRACE="$d/trace" STUB_STATE="$d" \
  STUB_AGENTHOST_LOGS="$BOOT_MARKER" BOOTSTRAP_LOG_TIMEOUT=3 \
  STUB_AGENT_UP_SLEEP=4 \
  POSTGRES_PASSWORD=pw PATH="$STUBBIN:$PATH" \
  bash "$d/deploy-jetson.sh" --image "$VALID_ID" --bootstrap >"$d/out" 2>&1 &
dpid=$!
for _ in $(seq 1 100); do [[ -f "$d/agent_up_started" ]] && break; sleep 0.1; done
kill -TERM "$dpid" 2>/dev/null || true
wait "$dpid"; rc=$?
check "bootstrap: SIGTERM before verify triggers rollback (exit 130)" "$rc" 130
grep -q 'AGENTHOST_STOP' "$d/trace" 2>/dev/null \
  && pass "bootstrap SIGTERM: stop issued in fresh rollback" \
  || fail "bootstrap SIGTERM: stop not found in trace"
grep -q 'AGENTHOST_RM' "$d/trace" 2>/dev/null \
  && pass "bootstrap SIGTERM: rm issued in fresh rollback" \
  || fail "bootstrap SIGTERM: rm not found in trace"
[[ ! -f "$d/.rollback-image" ]] \
  && pass "bootstrap SIGTERM: rollback file cleaned up" \
  || fail "bootstrap SIGTERM: rollback file not cleaned up"

# ---------------------------------------------------------------------------
echo "== No leaked docker containers / networks / volumes =="
if [[ $HAVE_DOCKER -eq 1 ]]; then
  c1="$(inv c)"; v1="$(inv v)"; n1="$(inv n)"
  [[ "$c1" == "$INV_C0" ]] && pass "no container leak" \
    || fail "container leak: $(comm -13 <(printf '%s\n' "$INV_C0") <(printf '%s\n' "$c1") | tr '\n' ' ')"
  [[ "$v1" == "$INV_V0" ]] && pass "no volume leak" \
    || fail "volume leak: $(comm -13 <(printf '%s\n' "$INV_V0") <(printf '%s\n' "$v1") | tr '\n' ' ')"
  [[ "$n1" == "$INV_N0" ]] && pass "no network leak" \
    || fail "network leak: $(comm -13 <(printf '%s\n' "$INV_N0") <(printf '%s\n' "$n1") | tr '\n' ' ')"
else
  skip "docker inventory leak guard (docker unavailable)"
fi

# ---------------------------------------------------------------------------
echo ""
echo "Results: ${PASS} passed, ${FAIL} failed"
[[ $FAIL -eq 0 ]]
