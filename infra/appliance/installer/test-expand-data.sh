#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
expand="$script_dir/rootfs/usr/libexec/lucia/lucia-expand-data"
work_dir="$(mktemp -d)"
loop_device=""

cleanup() {
    if [[ -n "$loop_device" ]]; then
        partx --delete "$loop_device" 2>/dev/null || true
        udevadm settle 2>/dev/null || true
        losetup --detach "$loop_device" 2>/dev/null || true
    fi
    rm -rf "$work_dir"
}
trap cleanup EXIT

truncate --size 2G "$work_dir/disk.img"
sgdisk \
    --new=1:2048:+100M \
    --change-name=1:APP \
    --new=2:0:+100M \
    --change-name=2:APP_b \
    --new=17:0:+100M \
    --change-name=17:LUCIA \
    --new=18:0:+200M \
    --change-name=18:LUCIA_DATA \
    "$work_dir/disk.img" >/dev/null
loop_device="$(losetup --find --show --partscan "$work_dir/disk.img")"
udevadm settle
loop_name="${loop_device#/dev/}"
while read -r name major_minor; do
    [[ "$name" == "$loop_name" ]] && continue
    major="${major_minor%:*}"
    minor="${major_minor#*:}"
    rm -f "/dev/$name"
    mknod "/dev/$name" b "$major" "$minor"
done < <(lsblk --raw --noheadings --output NAME,MAJ:MIN "$loop_device")
mkfs.ext4 -q -F "${loop_device}p18"

before="$(blockdev --getsize64 "${loop_device}p18")"
"$expand" "$loop_device"
after="$(blockdev --getsize64 "${loop_device}p18")"
filesystem_bytes="$(
    dumpe2fs -h "${loop_device}p18" 2>/dev/null \
        | awk '
            /Block count:/ { blocks = $3 }
            /Block size:/ { size = $3 }
            END { print blocks * size }
        '
)"

(( before < after ))
(( after > 1024 * 1024 * 1024 ))
(( filesystem_bytes > 1024 * 1024 * 1024 ))

echo "PASS: LUCIA_DATA expands to use remaining target storage"
