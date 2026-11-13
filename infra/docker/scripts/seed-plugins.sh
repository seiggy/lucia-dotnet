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

set -eu

SOURCE_DIR="${1:-/app/plugins}"
TARGET_DIR="${2:-/plugins-data}"
MARKER='.lucia-seeded'

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

  if [ -d "$dest" ]; then
    rm -rf "$dest"
    replaced=$((replaced + 1))
  else
    seeded=$((seeded + 1))
  fi
  mv "$staging" "$dest"
  echo "seed: seed  $name (from image)"
done

echo "Seed complete: $seeded seeded, $replaced replaced, $kept kept. Contents:"
ls "$TGT_ABS"
