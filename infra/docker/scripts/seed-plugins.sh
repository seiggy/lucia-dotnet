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
# drop its staging dir inside a winner's destination. A hard-killed holder
# (SIGKILL skips the cleanup trap) leaves a stale lock; a lock whose contents
# are all older than LUCIA_SEED_LOCK_STALE_MINUTES (default 5) is broken and
# re-taken, otherwise waiters fail loudly after LUCIA_SEED_LOCK_TIMEOUT_SECONDS
# (default 60).

set -eu

SOURCE_DIR="${1:-/app/plugins}"
TARGET_DIR="${2:-/plugins-data}"
MARKER='.lucia-seeded'

# Concurrent-seeder safety (shared ReadWriteMany volume, replicaCount > 1):
# per-plugin mkdir locks under TARGET_DIR/.lucia-seed-locks/ serialize the
# destination swap across pods. Tweak via environment if volumes are slow.
LOCK_TIMEOUT_SECONDS="${LUCIA_SEED_LOCK_TIMEOUT_SECONDS:-60}"
LOCK_STALE_MINUTES="${LUCIA_SEED_LOCK_STALE_MINUTES:-5}"
CURRENT_LOCK=''

cleanup_lock() {
  if [ -n "$CURRENT_LOCK" ]; then rm -rf "$CURRENT_LOCK"; fi
}
trap cleanup_lock EXIT INT TERM

# take_plugin_lock <name>: acquire the plugin's lock, breaking stale ones left
# behind by hard-killed holders. Returns 1 after waiting LOCK_TIMEOUT_SECONDS.
take_plugin_lock() {
  lock="$TGT_ABS/.lucia-seed-locks/$1.lucia-seed.lock"
  mkdir -p "$TGT_ABS/.lucia-seed-locks"
  attempts=0
  while :; do
    if [ -d "$lock" ] && [ -n "$(find "$lock" -maxdepth 1 -mmin +"$LOCK_STALE_MINUTES" 2>/dev/null)" ]; then
      echo "seed: clearing stale lock for '$1' (no live holder for > ${LOCK_STALE_MINUTES} min)"
      rm -rf "$lock"
    fi
    if mkdir "$lock" 2>/dev/null; then
      CURRENT_LOCK="$lock"
      { hostname 2>/dev/null || echo local; } >"$lock/owner"
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
  if [ -n "$CURRENT_LOCK" ]; then
    rm -rf "$CURRENT_LOCK"
    CURRENT_LOCK=''
  fi
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

echo "Seed complete: $seeded seeded, $replaced replaced, $kept kept. Contents:"
ls "$TGT_ABS"
