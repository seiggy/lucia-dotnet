#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d)"
loop_device=""
cleanup() {
    if mountpoint -q "$work/root"; then
        umount "$work/root"
    fi
    [[ -z "$loop_device" ]] || losetup --detach "$loop_device"
    rm -rf "$work"
}
trap cleanup EXIT
[[ "$EUID" == 0 ]] || { printf 'Run image verification tests as root.\n' >&2; exit 1; }

truncate --size 64M "$work/installer.img"
sgdisk --new=1:2048:0 --change-name=1:APP "$work/installer.img" >/dev/null
loop_device="$(losetup --find --show --partscan "$work/installer.img")"
udevadm settle
mkdir "$work/root"
mkfs.ext4 -q -F "${loop_device}p1"
mount "${loop_device}p1" "$work/root"
root="$work/root"
mkdir -p "$root/etc/ssh/sshd_config.d" "$root/etc/systemd/journald.conf.d" \
    "$root/etc/lucia-installer" "$root/usr/lib/systemd/system" \
    "$root/usr/libexec/lucia" "$root/opt/lucia-installer/app" "$root/var/lib"
for script in lucia-installer-control lucia-network-bootstrap; do
    install -m 0755 "$script_dir/rootfs/usr/libexec/lucia/$script" "$root/usr/libexec/lucia/$script"
done
install -m 0755 "$script_dir/../rootfs/usr/libexec/lucia/lucia-rootfs-ab-check" \
    "$root/usr/libexec/lucia/lucia-rootfs-ab-check"
printf 'User=root\n' > "$root/usr/lib/systemd/system/lucia-installer-host.service"
printf 'Appliance__ControlPath=/usr/libexec/lucia/lucia-installer-control\nAppliance__ControlCommand=\n' \
    > "$root/etc/lucia-installer/installer.env"
printf '# Use the hardware-tested fallback SSID.\n' > "$root/etc/lucia-installer/bootstrap.env"
chmod 0600 "$root/etc/lucia-installer/bootstrap.env"
printf 'lucia-recovery:x:1000:1000::/home/lucia-recovery:/bin/bash\n' > "$root/etc/passwd"
printf 'sudo:x:27:lucia-recovery\n' > "$root/etc/group"
printf 'PermitRootLogin no\n' > "$root/etc/ssh/sshd_config.d/90-lucia-recovery.conf"
printf 'Storage=persistent\n' > "$root/etc/systemd/journald.conf.d/lucia.conf"
touch "$root/opt/lucia-installer/app/lucia.InstallerHost"
umount "$root"
losetup --detach "$loop_device"
loop_device=""

# A mounted image must not inherit the builder's firmware checker.
printf '#!/bin/sh\nprintf "unexpected host probe\\n" > "%s"\nexit 64\n' "$work/host-probed" \
    > "$work/host-checker"
chmod +x "$work/host-checker"
LUCIA_ROOTFS_AB_CHECK_PATH="$work/host-checker" \
    bash "$script_dir/verify-built-image.sh" "$work/installer.img"
[[ ! -e "$work/host-probed" ]]
printf 'PASS: built-image status verification is independent of host firmware\n'
