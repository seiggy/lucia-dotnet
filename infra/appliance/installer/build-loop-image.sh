#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: build-loop-image.sh BSP_DIR OUTPUT_IMAGE [SIZE]

Builds a raw disk image from an existing NVIDIA initrd-flash package.
This is a host-side test helper; production flashing uses a real NVMe device.
EOF
}

[[ $# -ge 2 && $# -le 3 ]] || {
    usage >&2
    exit 2
}
[[ "$EUID" -eq 0 ]] || {
    printf 'Error: root privileges are required\n' >&2
    exit 1
}

bsp_dir="$(realpath "$1")"
output_image="$(realpath -m "$2")"
image_size="${3:-64G}"
images_dir="$bsp_dir/tools/kernel_flash/images"
external_dir="$images_dir/external"
flash_script="$images_dir/l4t_flash_from_kernel.sh"
flash_index="$external_dir/flash.idx"
script_backup=""
backup_ready=false
loop_device=""

cleanup() {
    if [[ -n "$loop_device" ]]; then
        losetup --detach "$loop_device" 2>/dev/null || true
    fi
    if [[ "$backup_ready" == true ]]; then
        cp "$script_backup" "$flash_script"
    fi
    [[ -z "$script_backup" ]] || rm -f "$script_backup"
}
trap cleanup EXIT

for command in blockdev dd file losetup partx parted sgdisk udevadm xxd; do
    command -v "$command" >/dev/null || {
        printf 'Error: required command is missing: %s\n' "$command" >&2
        exit 1
    }
done
[[ -f "$flash_script" && -f "$flash_index" ]] || {
    printf 'Error: NVIDIA flash package is incomplete\n' >&2
    exit 1
}

script_backup="$(mktemp)"
cp "$flash_script" "$script_backup"
backup_ready=true
[[ "$(grep -Ec 'disk="\$\{(ext_dev|device)%p\*\}"' "$flash_script")" -eq 2 ]] || {
    printf 'Error: NVIDIA device parser no longer matches the expected version\n' >&2
    exit 1
}
sed -i \
    -e 's#disk="${ext_dev%p[*]}"#if [[ "${ext_dev}" = loop* ]]; then disk="${ext_dev}"; else disk="${ext_dev%p*}"; fi#' \
    -e 's#disk="${device%p[*]}"#if [[ "${device}" = loop* ]]; then disk="${device}"; else disk="${device%p*}"; fi#' \
    "$flash_script"
[[ "$(grep -c '= loop\*' "$flash_script")" -eq 2 ]] || {
    printf 'Error: failed to add loop-device support\n' >&2
    exit 1
}

write_index_image() {
    local partition_name="$1"
    local line
    local offset
    local filename
    local file_size

    line="$(grep -m1 ":${partition_name}," "$flash_index")"
    offset="$(printf '%s' "$line" | cut -d, -f3 | tr -d ' ')"
    filename="$(printf '%s' "$line" | cut -d, -f5 | tr -d ' ')"
    file_size="$(printf '%s' "$line" | cut -d, -f6 | tr -d ' ')"
    dd \
        if="$external_dir/$filename" \
        of="$loop_device" \
        bs=1 \
        seek="$offset" \
        count="$file_size" \
        conv=notrunc \
        status=none
}

rm -f "$output_image"
truncate --size "$image_size" "$output_image"
loop_device="$(losetup --find --show "$output_image")"

write_index_image master_boot_record
write_index_image primary_gpt
write_index_image secondary_gpt
partx --add "$loop_device"

loop_name="${loop_device#/dev/}"
while read -r name major_minor; do
    [[ "$name" == "$loop_name" ]] && continue
    major="${major_minor%:*}"
    minor="${major_minor#*:}"
    [[ -e "/dev/$name" ]] || mknod "/dev/$name" b "$major" "$minor"
done < <(lsblk --raw --noheadings --output NAME,MAJ:MIN "$loop_device")

(
    cd "$images_dir"
    export EXTDEV_ON_HOST="$loop_name"
    export EXTDEV_ON_TARGET=nvme0n1
    ./l4t_flash_from_kernel.sh --direct
)

sync
sgdisk --verify "$loop_device"

printf 'image=%s\n' "$output_image"
printf 'logical_bytes=%s\n' "$(stat --format '%s' "$output_image")"
