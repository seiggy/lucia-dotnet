#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 1 && -f "$1" ]] || {
    printf 'Usage: verify-built-image.sh IMAGE\n' >&2
    exit 2
}
[[ "$EUID" -eq 0 ]] || {
    printf 'Error: root privileges are required\n' >&2
    exit 1
}

image="$(realpath "$1")"
work_dir="$(mktemp -d)"
root="$work_dir/root"
loop_device=""

cleanup() {
    mountpoint -q "$root" && umount "$root" || true
    [[ -z "$loop_device" ]] || losetup --detach "$loop_device" 2>/dev/null || true
    rm -rf "$work_dir"
}
trap cleanup EXIT

for command in bash losetup mknod mount mountpoint python3 stat udevadm umount; do
    command -v "$command" >/dev/null || {
        printf 'Error: required command is missing: %s\n' "$command" >&2
        exit 1
    }
done

mkdir "$root"
loop_device="$(losetup --read-only --find --show --partscan "$image")"
partition_name="${loop_device#/dev/}p1"
udevadm settle
[[ -r "/sys/class/block/$partition_name/dev" ]] || {
    printf 'Error: installer root partition is unavailable\n' >&2
    exit 1
}
read -r major minor < <(
    tr ':' ' ' < "/sys/class/block/$partition_name/dev"
)
mknod "$work_dir/root-partition" b "$major" "$minor"
mount -o ro "$work_dir/root-partition" "$root"

[[ "$(stat --format '%u:%g' \
    "$root/usr/libexec/lucia/lucia-installer-control")" == "0:0" ]]
grep -Fqx 'User=root' \
    "$root/usr/lib/systemd/system/lucia-installer-host.service"
grep -Fqx \
    'Appliance__ControlPath=/usr/libexec/lucia/lucia-installer-control' \
    "$root/etc/lucia-installer/installer.env"
grep -Fqx 'Appliance__ControlCommand=' \
    "$root/etc/lucia-installer/installer.env"

cat > "$work_dir/nmcli" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" == *"-g WIFI-PROPERTIES.AP device show wlan0"* ]]; then
    printf 'yes\n'
elif [[ "$*" == *"-g GENERAL.STATE connection show lucia-setup"* ]]; then
    printf 'activated\n'
elif [[ "$*" == *"--fields DEVICE,TYPE device status"* ]]; then
    printf 'wlan0:wifi\n'
fi
EOF
cat > "$work_dir/iptables" <<'EOF'
#!/usr/bin/env bash
[[ "$1" != "-C" ]]
EOF
chmod +x "$work_dir/nmcli" "$work_dir/iptables"

LUCIA_BOOTSTRAP_ENV="$root/etc/lucia-installer/bootstrap.env" \
LUCIA_CONNECTION_PATH="$work_dir/lucia-setup.nmconnection" \
LUCIA_DEVICE_SERIAL_PATH="$work_dir/missing-serial" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
    bash "$root/usr/libexec/lucia/lucia-network-bootstrap"
grep -Fqx 'ssid=Lucia-Setup' "$work_dir/lucia-setup.nmconnection"

mkdir "$work_dir/state"
status="$(
    LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        python3 "$root/usr/libexec/lucia/lucia-installer-control" status
)"
[[ "$status" == '{"phase":"waiting-for-configuration"}' ]]

printf 'PASS: built installer image starts captive setup and reports status\n'
