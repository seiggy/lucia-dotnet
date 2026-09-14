#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: build-sd-image.sh BSP_DIR OUTPUT_IMAGE

Builds the Jetson Orin Nano Super microSD installer image from a prepared
Linux_for_Tegra rootfs.
EOF
}

[[ $# -eq 2 ]] || {
    usage >&2
    exit 2
}
[[ "$EUID" -eq 0 ]] || {
    printf 'Error: root privileges are required\n' >&2
    exit 1
}

bsp_dir="$(realpath "$1")"
output_image="$(realpath -m "$2")"
image_creator="$bsp_dir/tools/jetson-disk-image-creator.sh"
real_losetup="$(command -v losetup)"
wrapper_dir="$(mktemp -d)"

cleanup() {
    rm -rf "$wrapper_dir"
}
trap cleanup EXIT

for command in lsblk mknod partx sgdisk; do
    command -v "$command" >/dev/null || {
        printf 'Error: required command is missing: %s\n' "$command" >&2
        exit 1
    }
done
[[ -x "$image_creator" ]] || {
    printf 'Error: NVIDIA disk image creator is missing\n' >&2
    exit 1
}

cat > "$wrapper_dir/losetup" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ " $* " != *" --show "* \
    || (" $* " != *" -P "* && " $* " != *" --partscan "*) ]]; then
    exec "$REAL_LOSETUP" "$@"
fi

loop_device="$("$REAL_LOSETUP" "$@")"
loop_name="${loop_device#/dev/}"
partx --update "$loop_device"
while read -r name major_minor; do
    [[ "$name" == "$loop_name" ]] && continue
    major="${major_minor%:*}"
    minor="${major_minor#*:}"
    rm -f "/dev/$name"
    mknod "/dev/$name" b "$major" "$minor"
done < <(lsblk --raw --noheadings --output NAME,MAJ:MIN "$loop_device")
printf '%s\n' "$loop_device"
EOF
chmod 0755 "$wrapper_dir/losetup"

rm -f "$output_image"
export REAL_LOSETUP="$real_losetup"
PATH="$wrapper_dir:$PATH" \
    "$image_creator" \
        -o "$output_image" \
        -b jetson-orin-nano-devkit-super \
        -d SD

printf 'image=%s\n' "$output_image"
printf 'logical_bytes=%s\n' "$(stat --format '%s' "$output_image")"
