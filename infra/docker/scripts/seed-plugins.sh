#!/bin/sh
# seed-plugins.sh — sync image-bundled plugins into the persistent plugins volume.
#
# Runs as the pod's seed init container (or standalone for testing):
#   seed-plugins.sh [SOURCE_DIR] [TARGET_DIR]
#     SOURCE_DIR  default /app/plugins    (bundled plugins baked into the image)
#     TARGET_DIR  default /plugins-data   (the PVC mount to seed)
#
# Invariant: a plugin directory in TARGET_DIR is under image management if and
# only if it contains a .lucia-seeded marker file, which only this script ever
# writes. Consequences:
#   - marked dir      -> replaced wholesale from the image on every run, so
#                        bundled-plugin code updates flow through on image
#                        upgrades and corrupted/partial copies self-heal;
#   - missing dir     -> copied fresh, then marked;
#   - unmarked dir    -> never touched (repository-installed or user-managed
#                        plugins), even when its name collides with a bundled
#                        plugin.
#
# Replacement is done via a staging copy plus atomic rename within TARGET_DIR,
# so an interrupted run always leaves either the previous complete copy or a
# missing directory — never a partial one at the final path. Known edge: a pod
# killed mid-seed can leave its hidden .<name>.<host>.lucia-seed-tmp directory
# behind; the next seed from that same host clears it, leftovers from other
# hosts are inert dot-directories — delete by hand if a plugin scan ever picks
# one up.
#
# Concurrent seeders on a shared ReadWriteMany volume (multiple replicas) are
# serialized by a per-plugin mkdir lock under TARGET_DIR/.lucia-seed-locks/:
# the "inspect destination + swap" section is critical, so a late mv can never
# drop its staging dir inside a winner's destination. Liveness: each holder
# refreshes a heartbeat file in its lock every LOCK_HEARTBEAT_SECONDS
# (default 30s), and a lock counts as stale only when EVERY file in it — the
# heartbeat above all — is older than LOCK_STALE_MINUTES (default 5), so a slow
# but live holder keeps its lock. Takeover of a stale lock is a single atomic
# rename() to a unique graveyard name, so exactly one waiter can ever move a
# given lock aside; waiters that lose that rename fall through to the plain
# mkdir and compete there (also atomic). The graveyard is re-checked before it
# is deleted: if a heartbeat landed mid-takeover, the holder is alive and the
# lock is put back. Otherwise waiters fail loudly after LOCK_TIMEOUT_SECONDS
# (default 60).

set -eu

SOURCE_DIR="${1:-/app/plugins}"
TARGET_DIR="${2:-/plugins-data}"
MARKER='.lucia-seeded'

# Concurrent-seeder safety (shared ReadWriteMany volume, replicaCount > 1):
# per-plugin mkdir locks under TARGET_DIR/.lucia-seed-locks/ serialize the
# destination swap across pods. A lock is stale only when EVERY file in it is
# older than LOCK_STALE_MINUTES — the holder's heartbeat (refreshed every
# LOCK_HEARTBEAT_SECONDS while it lives) keeps a slow-but-live holder's lock
# fresh, so liveness is signaled, not assumed from elapsed time. Breaking a
# stale lock is one atomic rename() to a per-process graveyard name, so
# exactly one waiter can ever take a given lock away; waiters that lose that
# rename fall through to the plain mkdir and compete there (also atomic). The
# winner re-checks the moved-away lock: if a heartbeat landed between the
# staleness test and the move, the holder is alive after all and it goes back.
# A lock is only removed by a seeder whose owner file still matches its own
# ID; leftover graveyards are swept at the end of each run. Tweak via
# environment if volumes are slow.
LOCK_TIMEOUT_SECONDS="${LUCIA_SEED_LOCK_TIMEOUT_SECONDS:-60}"
LOCK_STALE_MINUTES="${LUCIA_SEED_LOCK_STALE_MINUTES:-5}"
LOCK_HEARTBEAT_SECONDS="${LUCIA_SEED_LOCK_HEARTBEAT_SECONDS:-30}"
CURRENT_LOCK=''
HEARTBEAT_PID=''
OWNER_ID="$(hostname 2>/dev/null || echo local).$$"

stop_heartbeat() {
  if [ -n "$HEARTBEAT_PID" ]; then
    kill "$HEARTBEAT_PID" 2>/dev/null || :
    wait "$HEARTBEAT_PID" 2>/dev/null || :
    HEARTBEAT_PID=''
  fi
}

cleanup_lock() {
  stop_heartbeat
  # Only remove a lock we demonstrably own: if it vanished or was re-taken by
  # another seeder, its owner file holds their ID and we must leave it alone.
  if [ -n "$CURRENT_LOCK" ] && [ -d "$CURRENT_LOCK" ] \
     && grep -qxF "$OWNER_ID" "$CURRENT_LOCK/owner" 2>/dev/null; then
    rm -rf "$CURRENT_LOCK"
  fi
  CURRENT_LOCK=''
}
trap cleanup_lock EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# lock_is_stale <dir>: true iff the dir contains at least one file and every
# file in it is older than LOCK_STALE_MINUTES — i.e. no live heartbeat (or
# owner write) has been observed within that window. An empty or fresh lock is
# never stale: the holder is mid-acquire or still beating.
lock_is_stale() {
  [ -n "$(find "$1" -mindepth 1 -maxdepth 1 2>/dev/null)" ] || return 1
  [ -z "$(find "$1" -mindepth 1 -maxdepth 1 ! -mmin +"$LOCK_STALE_MINUTES" 2>/dev/null)" ]
}

# take_plugin_lock <name>: acquire the plugin's lock, taking over stale ones
# left behind by hard-killed holders. Returns 1 after waiting LOCK_TIMEOUT_SECONDS.
take_plugin_lock() {
  lock="$TGT_ABS/.lucia-seed-locks/$1.lucia-seed.lock"
  mkdir -p "$TGT_ABS/.lucia-seed-locks"
  attempts=0
  while :; do
    if [ -d "$lock" ] && lock_is_stale "$lock"; then
      graveyard="$lock.stale.$$"
      rm -rf "$graveyard" 2>/dev/null || :
      # rename() is atomic: exactly one concurrent waiter can move this exact
      # path aside, so the takeover is single-actor by construction. A waiter
      # whose rename fails simply falls through to the mkdir below.
      if mv "$lock" "$graveyard" 2>/dev/null; then
        echo "seed: taking over stale lock for '$1' (no live heartbeat for > ${LOCK_STALE_MINUTES} min)"
        # Re-check the moved-away lock: if a heartbeat landed between the
        # staleness test and the rename, the holder is alive after all — put
        # it back (or drop it, if someone already re-created the lock) and
        # keep waiting instead of breaking a live hold.
        if [ -n "$(find "$graveyard" -mindepth 1 -maxdepth 1 ! -mmin +"$LOCK_STALE_MINUTES" 2>/dev/null)" ]; then
          echo "seed: lock for '$1' showed a live heartbeat mid-takeover — restoring it"
          if [ ! -e "$lock" ]; then
            mv "$graveyard" "$lock" 2>/dev/null || :
          fi
          rm -rf "$graveyard" 2>/dev/null || :
        else
          rm -rf "$graveyard" 2>/dev/null || :
        fi
      fi
    fi
    # The lock dir's parent may have been rmdir'd by a finishing seeder's end
    # of run tidy; re-create it so the retry below has somewhere to mkdir.
    mkdir -p "$TGT_ABS/.lucia-seed-locks" 2>/dev/null || :
    if mkdir "$lock" 2>/dev/null; then
      CURRENT_LOCK="$lock"
      printf '%s\n' "$OWNER_ID" >"$lock/owner"
      date -u '+%Y-%m-%dT%H:%M:%SZ' >"$lock/heartbeat" 2>/dev/null || :
      # Beat loop: refresh the heartbeat while we hold the lock. The redirect
      # fails (and the loop exits) if our lock dir is taken away from us, so a
      # broken holder can never keep signaling liveness into someone else's lock.
      (
        while :; do
          sleep "$LOCK_HEARTBEAT_SECONDS"
          date -u '+%Y-%m-%dT%H:%M:%SZ' >"$lock/heartbeat" 2>/dev/null || exit 0
        done
      ) &
      HEARTBEAT_PID=$!
      return 0
    fi
    attempts=$((attempts + 1))
    if [ "$attempts" -ge $((LOCK_TIMEOUT_SECONDS * 5)) ]; then
      return 1
    fi
    sleep 0.2
  done
}

release_plugin_lock() {
  cleanup_lock
}

[ -d "$SOURCE_DIR" ] || { echo "seed: source dir '$SOURCE_DIR' does not exist — refusing to seed (broken image?)" >&2; exit 1; }

# Resolve to absolute paths for the equality guard below.
abs() { cd "$1" && pwd; }
SRC_ABS="$(abs "$SOURCE_DIR")"
mkdir -p "$TARGET_DIR"
TGT_ABS="$(abs "$TARGET_DIR")"
if [ "$SRC_ABS" = "$TGT_ABS" ]; then
  echo "seed: SOURCE_DIR and TARGET_DIR must differ ('$SRC_ABS') — refusing to run." >&2
  exit 1
fi

echo "Seeding bundled plugins from '$SRC_ABS' into '$TGT_ABS' (marker-based)..."

seeded=0
replaced=0
kept=0

for src in "$SRC_ABS"/*/; do
  # The glob may not match anything; guard the trailing-slash expansion.
  [ -d "$src" ] || continue
  name="${src%/}"; name="${name##*/}"

  if [ ! -f "${src}plugin.cs" ]; then
    echo "seed: skip  $name (no plugin.cs entry point)"
    continue
  fi

  dest="$TGT_ABS/$name"
  if [ -d "$dest" ] && [ ! -e "$dest/$MARKER" ]; then
    kept=$((kept + 1))
    echo "seed: keep  $name (unmarked — user-managed, left untouched)"
    continue
  fi

  # Staging name includes the host (pod) name so concurrent seeders on a shared
  # ReadWriteMany volume never clobber each other's in-flight copies.
  staging="$TGT_ABS/.$name.${HOSTNAME:-local}.lucia-seed-tmp"
  rm -rf "$staging"
  cp -r "${src}." "$staging"
  if [ ! -f "$staging/plugin.cs" ]; then
    echo "seed: abort $name (staging copy is missing plugin.cs)" >&2
    rm -rf "$staging"
    exit 1
  fi
  # Marker is written inside the staging copy BEFORE it becomes visible at the
  # final path, so a directory with the final name always carries its marker.
  date -u '+%Y-%m-%dT%H:%M:%SZ' > "$staging/$MARKER"

  # Serialize "inspect destination + swap" per plugin: without the lock, two
  # seeders on a shared volume could both see the dir missing and the late mv
  # would drop its staging dir INSIDE the winner's destination.
  if ! take_plugin_lock "$name"; then
    echo "seed: could not take the lock for '$name' within ${LOCK_TIMEOUT_SECONDS}s (another seeder is holding it?) — refusing to risk a double-swap." >&2
    rm -rf "$staging"
    exit 1
  fi
  # Re-verify we still own the lock right before touching the destination. If
  # a takeover moved it away mid-flight (our heartbeat stalled past the stale
  # window), abort instead of racing another seeder's swap: worst case the
  # next run re-seeds the dir — never a partial one at the final path.
  if [ ! -d "$CURRENT_LOCK" ]; then
    echo "seed: lost the lock for '$name' before the swap (taken over?) — aborting to avoid a double-swap." >&2
    rm -rf "$staging"
    exit 1
  fi
  if [ -d "$dest" ]; then
    rm -rf "$dest"
    replaced=$((replaced + 1))
  else
    seeded=$((seeded + 1))
  fi
  mv "$staging" "$dest"
  release_plugin_lock
  echo "seed: seed  $name (from image)"
done

# Tidy up: remove leftover lock graveyards (a holder SIGKILLed between its
# rename and its cleanup, or an abandoned takeover) once everything in them is
# stale, and drop the now-empty lock dir. Concurrent seeders are unaffected:
# a live graveyard's contents are never all old, and rmdir only succeeds on
# an empty dir.
for g in "$TGT_ABS/.lucia-seed-locks"/*.stale.*; do
  if [ -d "$g" ] && lock_is_stale "$g"; then
    rm -rf "$g" 2>/dev/null || :
  fi
done
rmdir "$TGT_ABS/.lucia-seed-locks" 2>/dev/null || :

echo "Seed complete: $seeded seeded, $replaced replaced, $kept kept. Contents:"
ls "$TGT_ABS"
