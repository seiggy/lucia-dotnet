#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: finalize-loop-image.sh IMAGE [LUCIA_PARTITION_SIZE] [BUNDLE_ROOT]

Moves Lucia application and data files out of the A/B root filesystems into
dedicated persistent partitions. BUNDLE_ROOT may provide those files when the
root filesystems were built without application data.
EOF
}

[[ $# -ge 1 && $# -le 3 ]] || {
    usage >&2
    exit 2
}
[[ "$EUID" -eq 0 ]] || {
    printf 'Error: root privileges are required\n' >&2
    exit 1
}

image="$(realpath "$1")"
lucia_partition_size="${2:-6G}"
bundle_root=""
if [[ -n "${3:-}" ]]; then
    bundle_root="$(realpath "$3")"
fi
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
rootfs_overlay="$script_dir/../rootfs"
lucia_partition_guid="${LUCIA_PARTITION_GUID:-95270b8b-a9a3-4775-8921-46d9f5d822fa}"
data_partition_guid="${LUCIA_DATA_PARTITION_GUID:-9348df64-dff5-4a35-ab34-e09c6941ac24}"
lucia_filesystem_uuid="${LUCIA_FILESYSTEM_UUID:-032d7831-4eee-473c-8632-65378ac96cba}"
data_filesystem_uuid="${LUCIA_DATA_FILESYSTEM_UUID:-2226d852-2202-4f42-94da-dbde4d7a88f9}"
work="$(mktemp -d)"
loop_device=""
mounts=()

cleanup() {
    local cleanup_failed=0

    for ((index=${#mounts[@]} - 1; index >= 0; index--)); do
        if mountpoint -q "${mounts[$index]}" \
            && ! umount "${mounts[$index]}"; then
            cleanup_failed=1
        fi
    done
    if findmnt -rn -o TARGET \
        | awk -v root="$work" \
            '$0 == root || index($0, root "/") == 1 { found = 1 } END { exit !found }'; then
        printf 'Error: cleanup left active mounts under %s\n' "$work" >&2
        cleanup_failed=1
    fi
    if [[ -n "$loop_device" ]] \
        && ! losetup --detach "$loop_device" 2>/dev/null; then
        cleanup_failed=1
    fi
    if [[ "$cleanup_failed" -eq 0 ]]; then
        rm -rf --one-file-system -- "$work"
    fi
    return "$cleanup_failed"
}
on_exit() {
    local status=$?

    trap - EXIT
    cleanup || status=1
    exit "$status"
}
trap on_exit EXIT

for command in e2fsck findmnt fstrim losetup mkfs.ext4 mountpoint partx sgdisk umount; do
    command -v "$command" >/dev/null || {
        printf 'Error: required command is missing: %s\n' "$command" >&2
        exit 1
    }
done
[[ -f "$image" ]] || {
    printf 'Error: image does not exist: %s\n' "$image" >&2
    exit 1
}
if [[ -n "$bundle_root" ]]; then
    [[ -d "$bundle_root/opt/lucia" && -d "$bundle_root/var/lib/lucia" ]] || {
        printf 'Error: bundle root is missing Lucia application or data files\n' >&2
        exit 1
    }
fi
[[ -f "$rootfs_overlay/var/lib/lucia/config/lucia.env" ]] || {
    printf 'Error: appliance rootfs overlay is incomplete\n' >&2
    exit 1
}

loop_device="$(losetup --find --show "$image")"
loop_name="${loop_device#/dev/}"

materialize_partitions() {
    partx --update "$loop_device"
    while read -r name major_minor; do
        [[ "$name" == "$loop_name" ]] && continue
        major="${major_minor%:*}"
        minor="${major_minor#*:}"
        rm -f "/dev/$name"
        mknod "/dev/$name" b "$major" "$minor"
    done < <(lsblk --raw --noheadings --output NAME,MAJ:MIN "$loop_device")
}

materialize_partitions
[[ -b "/dev/${loop_name}p1" && -b "/dev/${loop_name}p2" ]] || {
    printf 'Error: image does not contain APP and APP_b\n' >&2
    exit 1
}
if lsblk --noheadings --raw --output PARTLABEL "$loop_device" \
    | grep -Eq '^(LUCIA|LUCIA_DATA)$'; then
    printf 'Error: image already contains appliance partitions\n' >&2
    exit 1
fi

sgdisk \
    --new="17:0:+${lucia_partition_size}" \
    --change-name="17:LUCIA" \
    --typecode="17:8300" \
    --partition-guid="17:${lucia_partition_guid}" \
    --new="18:0:0" \
    --change-name="18:LUCIA_DATA" \
    --typecode="18:8300" \
    --partition-guid="18:${data_partition_guid}" \
    "$loop_device"
materialize_partitions

lucia_device="/dev/${loop_name}p17"
data_device="/dev/${loop_name}p18"
[[ -b "$lucia_device" && -b "$data_device" ]] || {
    printf 'Error: appliance partition devices were not created\n' >&2
    exit 1
}

E2FSPROGS_FAKE_TIME="${SOURCE_DATE_EPOCH:-946684800}" \
    mkfs.ext4 -q -F -L LUCIA -U "$lucia_filesystem_uuid" \
    -E "hash_seed=$lucia_filesystem_uuid,lazy_itable_init=0,lazy_journal_init=0" \
    "$lucia_device"
E2FSPROGS_FAKE_TIME="${SOURCE_DATE_EPOCH:-946684800}" \
    mkfs.ext4 -q -F -L LUCIA_DATA -U "$data_filesystem_uuid" \
    -E "hash_seed=$data_filesystem_uuid,lazy_itable_init=0,lazy_journal_init=0" \
    "$data_device"

for name in app app_b lucia data; do
    mkdir "$work/$name"
done
mount "/dev/${loop_name}p1" "$work/app"
mounts+=("$work/app")
mount "/dev/${loop_name}p2" "$work/app_b"
mounts+=("$work/app_b")
mount "$lucia_device" "$work/lucia"
mounts+=("$work/lucia")
mount "$data_device" "$work/data"
mounts+=("$work/data")

if [[ -n "$bundle_root" ]]; then
    cp -a "$bundle_root/opt/lucia/." "$work/lucia/"
    cp -a "$bundle_root/var/lib/lucia/." "$work/data/"
else
    cp -a "$work/app/opt/lucia/." "$work/lucia/"
    cp -a "$work/app/var/lib/lucia/." "$work/data/"
fi
mkdir -p "$work/data/redis"
chown -R 1100:1100 "$work/data"
chown root:root "$work/data"
chmod 0755 "$work/data"
chown 1101:1101 "$work/data/redis"
mkdir -p "$work/data/config"
cp \
    "$rootfs_overlay/var/lib/lucia/config/lucia.env" \
    "$work/data/config/lucia.env"
chown root:1100 "$work/data/config" "$work/data/config/lucia.env"
chmod 0750 "$work/data/config"
chmod 0640 "$work/data/config/lucia.env"

for slot in "$work/app" "$work/app_b"; do
    find "$slot/opt/lucia" -mindepth 1 -delete
    find "$slot/var/lib/lucia" -mindepth 1 -delete

    cp "$rootfs_overlay/etc/lucia/redis.conf" "$slot/etc/lucia/redis.conf"
    cp \
        "$rootfs_overlay/usr/lib/systemd/system/lucia-agenthost.service" \
        "$slot/usr/lib/systemd/system/lucia-agenthost.service"
    cp \
        "$rootfs_overlay/usr/lib/systemd/system/lucia-redis.service" \
        "$slot/usr/lib/systemd/system/lucia-redis.service"
    cp \
        "$rootfs_overlay/usr/lib/sysusers.d/lucia.conf" \
        "$slot/usr/lib/sysusers.d/lucia.conf"
    cp \
        "$rootfs_overlay/usr/lib/tmpfiles.d/lucia.conf" \
        "$slot/usr/lib/tmpfiles.d/lucia.conf"
    grep -q 'PARTLABEL=LUCIA ' "$slot/etc/fstab" \
        || printf 'PARTLABEL=LUCIA /opt/lucia ext4 defaults,nodev,nosuid 0 2\n' \
            >> "$slot/etc/fstab"
    grep -q 'PARTLABEL=LUCIA_DATA ' "$slot/etc/fstab" \
        || printf 'PARTLABEL=LUCIA_DATA /var/lib/lucia ext4 defaults,nodev,nosuid 0 2\n' \
            >> "$slot/etc/fstab"
done

sync
for mountpoint in "${mounts[@]}"; do
    fstrim "$mountpoint"
done
for mountpoint in "${mounts[@]}"; do
    umount "$mountpoint"
done
mounts=()

e2fsck -fn "/dev/${loop_name}p1"
e2fsck -fn "/dev/${loop_name}p2"
e2fsck -fn "$lucia_device"
e2fsck -fn "$data_device"
sgdisk --verify "$loop_device"

printf 'image=%s\n' "$image"
printf 'lucia_partition_bytes=%s\n' "$(blockdev --getsize64 "$lucia_device")"
printf 'data_partition_bytes=%s\n' "$(blockdev --getsize64 "$data_device")"
