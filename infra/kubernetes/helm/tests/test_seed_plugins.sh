#!/usr/bin/env bash
# Behavioral tests for infra/docker/scripts/seed-plugins.sh.
#
# The script syncs image-bundled plugins (dirs containing a plugin.cs) from a
# source directory into a persistent-volume target directory:
#   - fresh target dir            -> copied, then marked (.lucia-seeded)
#   - marked target dir           -> replaced wholesale from the source
#                                    (image upgrades flow through; corrupted
#                                     or partial copies self-heal)
#   - unmarked target dir         -> never touched (repository-installed or
#                                    user-managed plugins), even when its name
#                                    collides with a bundled plugin
#
# Run: bash test_seed_plugins.sh   (plain POSIX sh fixtures, no docker needed)

set -u

TESTS_DIR="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$TESTS_DIR/../../../docker/scripts/seed-plugins.sh"
if [ ! -f "$SCRIPT" ]; then
  echo "FAIL: seed script not found at $SCRIPT" >&2
  exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
PASS=0
FAIL=0

pass() { PASS=$((PASS + 1)); echo "ok   - $1"; }
fail() { FAIL=$((FAIL + 1)); echo "FAIL - $1" >&2; }
assert() { # assert <description> <shell expression...>
  local desc="$1"; shift
  if "$@" >/dev/null 2>&1; then pass "$desc"; else fail "$desc"; fi
}

run_seed() { sh "$SCRIPT" "$1" "$2" >"$WORK/last.log" 2>&1; }

# ── Fixture builders ─────────────────────────────────────────────

make_source() { # $1 = source dir
  local s="$1"
  rm -rf "$s"; mkdir -p "$s"
  for p in brave-search metamcp searxng; do
    mkdir -p "$s/$p"
    printf 'plugin source for %s v1\n' "$p" >"$s/$p/plugin.cs"
    printf 'aux data for %s\n' "$p" >"$s/$p/data.txt"
  done
  # Things that must never be copied: a stray top-level file and a dir
  # without a plugin.cs entry point.
  echo "not a plugin" >"$s/README.md"
  mkdir -p "$s/not-a-plugin"
  echo "junk" >"$s/not-a-plugin/readme.txt"
}

MARKER='.lucia-seeded'

# ── T1: fresh volume gets every bundled plugin, marked ───────────

make_source "$WORK/src"
T="$WORK/t1-target"; mkdir -p "$T"
run_seed "$WORK/src" "$T"
assert "T1 fresh: exit 0" test $? -eq 0
for p in brave-search metamcp searxng; do
  assert "T1 fresh: $p copied" test -f "$T/$p/plugin.cs"
  assert "T1 fresh: $p aux file copied" cmp -s "$WORK/src/$p/data.txt" "$T/$p/data.txt"
  assert "T1 fresh: $p marked" test -f "$T/$p/$MARKER"
done
assert "T1 fresh: stray top-level file not copied" test ! -e "$T/README.md"
assert "T1 fresh: non-plugin dir not copied" test ! -e "$T/not-a-plugin"

# ── T2: image upgrade replaces marked dirs wholesale ─────────────

printf 'plugin source for searxng v2 (updated)\n' >"$WORK/src/searxng/plugin.cs"
run_seed "$WORK/src" "$T"
assert "T2 update: exit 0" test $? -eq 0
assert "T2 update: searxng plugin.cs is the new version" grep -q "v2 (updated)" "$T/searxng/plugin.cs"
assert "T2 update: searxng still marked" test -f "$T/searxng/$MARKER"
assert "T2 update: untouched bundled dir still matches image" cmp -s "$WORK/src/metamcp/plugin.cs" "$T/metamcp/plugin.cs"

# ── T3: unmarked (user-managed) dir is never touched ─────────────

mkdir -p "$T/my-custom-plugin/sub"
printf 'my own plugin\n' >"$T/my-custom-plugin/plugin.cs"
printf 'precious user data\n' >"$T/my-custom-plugin/sub/state.bin"
MTIME_BEFORE=$(stat -c %Y "$T/my-custom-plugin/plugin.cs")
run_seed "$WORK/src" "$T"
assert "T3 user-managed: exit 0" test $? -eq 0
assert "T3 user-managed: content unchanged" grep -q "my own plugin" "$T/my-custom-plugin/plugin.cs"
assert "T3 user-managed: no marker added" test ! -e "$T/my-custom-plugin/$MARKER"
assert "T3 user-managed: not rewritten (mtime stable)" test "$(stat -c %Y "$T/my-custom-plugin/plugin.cs")" -eq "$MTIME_BEFORE"

# ── T4: name collision with a bundled plugin stays user-managed ──

U="$WORK/t4-target"; make_source "$U-src"
rm -rf "$U"; mkdir -p "$U/searxng"
printf 'store-forked searxng, different content\n' >"$U/searxng/plugin.cs"
run_seed "$U-src" "$U"
assert "T4 collision: exit 0" test $? -eq 0
assert "T4 collision: store fork left byte-identical" grep -q "store-forked" "$U/searxng/plugin.cs"
assert "T4 collision: no marker added to the fork" test ! -e "$U/searxng/$MARKER"
assert "T4 collision: other bundled plugins still seeded" test -f "$U/brave-search/$MARKER"

# ── T5: corrupted marked dir is repaired from the image ───────────

rm -rf "$WORK/t1-target/metamcp/plugin.cs"
run_seed "$WORK/src" "$T"
assert "T5 repair: exit 0" test $? -eq 0
assert "T5 repair: deleted plugin.cs restored" cmp -s "$WORK/src/metamcp/plugin.cs" "$T/metamcp/plugin.cs"
assert "T5 repair: still marked" test -f "$T/metamcp/$MARKER"

# ── T6: interrupted seed (dir vanished) re-seeds cleanly ─────────

rm -rf "$T/brave-search"
run_seed "$WORK/src" "$T"
assert "T6 vanished dir: exit 0" test $? -eq 0
assert "T6 vanished dir: recreated from image" cmp -s "$WORK/src/brave-search/plugin.cs" "$T/brave-search/plugin.cs"
assert "T6 vanished dir: marked again" test -f "$T/brave-search/$MARKER"

# ── T7: idempotent — a second run changes nothing and keeps exit 0 ─

BEFORE=$(cd "$T" && find . -type f | sort | xargs md5sum)
run_seed "$WORK/src" "$T"
assert "T7 idempotent: exit 0" test $? -eq 0
AFTER=$(cd "$T" && find . -type f | sort | xargs md5sum)
assert "T7 idempotent: file tree unchanged (modulo marker timestamps)" \
  test "$(echo "$BEFORE" | grep -v "$MARKER")" = "$(echo "$AFTER" | grep -v "$MARKER")"

# ── T8: stale staging leftovers are cleared, not trusted ─────────

STALE_STAGING="$T/.searxng.${HOSTNAME:-local}.lucia-seed-tmp"
mkdir -p "$STALE_STAGING"
printf 'stale junk\n' >"$STALE_STAGING/plugin.cs"
run_seed "$WORK/src" "$T"
assert "T8 staging: exit 0" test $? -eq 0
assert "T8 staging: leftover staging dir gone" test ! -e "$STALE_STAGING"
assert "T8 staging: real content wins" grep -q "v2 (updated)" "$T/searxng/plugin.cs"

# ── T9: source == target is a hard error (misconfiguration) ──────

run_seed "$WORK/src" "$WORK/src"
assert "T9 guard: source==target fails loudly" test $? -ne 0

echo
echo "seed-plugins tests: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
