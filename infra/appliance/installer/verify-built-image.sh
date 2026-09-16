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

for directory in etc etc/ssh etc/ssh/sshd_config.d usr usr/lib usr/libexec var var/lib; do
    [[ "$(stat --format '%u:%g' "$root/$directory")" == "0:0" ]]
done
[[ "$(stat --format '%u:%g' \
    "$root/usr/libexec/lucia/lucia-installer-control")" == "0:0" ]]
[[ "$(stat --format '%u:%g' \
    "$root/usr/libexec/lucia/lucia-rootfs-ab-check")" == "0:0" ]]
[[ "$(stat --format '%u:%g' \
    "$root/opt/lucia-installer/app/lucia.InstallerHost")" == "0:0" ]]
grep -Fqx 'User=root' \
    "$root/usr/lib/systemd/system/lucia-installer-host.service"
grep -Fqx \
    'Appliance__ControlPath=/usr/libexec/lucia/lucia-installer-control' \
    "$root/etc/lucia-installer/installer.env"
grep -Fqx 'Appliance__ControlCommand=' \
    "$root/etc/lucia-installer/installer.env"
grep -Eq '^lucia-recovery:[^:]*:[^:]*:[^:]*:[^:]*:[^:]*:/bin/bash$' \
    "$root/etc/passwd"
grep -Eq '^sudo:[^:]*:[^:]*:([^,]+,)*lucia-recovery(,[^,]+)*$' \
    "$root/etc/group"
grep -Fqx 'PermitRootLogin no' \
    "$root/etc/ssh/sshd_config.d/90-lucia-recovery.conf"
! grep -q 'ForceCommand' \
    "$root/etc/ssh/sshd_config.d/90-lucia-recovery.conf"
grep -Fqx 'Storage=persistent' \
    "$root/etc/systemd/journald.conf.d/lucia.conf"
[[ "$(stat --format '%u:%g' \
    "$root/etc/ssh/sshd_config.d/90-lucia-recovery.conf")" == "0:0" ]]

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
cat > "$work_dir/nvbootctrl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
case "$*" in
    "-t rootfs is-rootfs-ab-enabled") exit "$LUCIA_VERIFY_AB_RESULT" ;;
    "-t rootfs get-current-slot") printf '%s\n' "$((LUCIA_VERIFY_AB_RESULT - 1))" ;;
    *) exit 64 ;;
esac
EOF
chmod +x "$work_dir/nvbootctrl"

LUCIA_BOOTSTRAP_ENV="$root/etc/lucia-installer/bootstrap.env" \
LUCIA_CONNECTION_PATH="$work_dir/lucia-setup.nmconnection" \
LUCIA_DEVICE_SERIAL_PATH="$work_dir/missing-serial" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
    bash "$root/usr/libexec/lucia/lucia-network-bootstrap"
grep -Fqx 'ssid=Lucia-Setup' "$work_dir/lucia-setup.nmconnection"

mkdir "$work_dir/state"
for capability in 0 1 2 64; do
    status="$(
        LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        LUCIA_ROOTFS_AB_CHECK_PATH="$root/usr/libexec/lucia/lucia-rootfs-ab-check" \
        LUCIA_NVBOOTCTRL_PATH="$work_dir/nvbootctrl" \
        LUCIA_VERIFY_AB_RESULT="$capability" \
            python3 "$root/usr/libexec/lucia/lucia-installer-control" status
    )"
    python3 - "$status" "$capability" <<'PY'
import json
import sys

status = json.loads(sys.argv[1])
assert status["phase"] == "waiting-for-configuration"
if sys.argv[2] in {"1", "2"}:
    assert "osUpdateWarning" not in status
else:
    assert "RootFS A/B" in status["osUpdateWarning"]
PY
done

printf 'PASS: built installer image starts captive setup and reports status\n'
