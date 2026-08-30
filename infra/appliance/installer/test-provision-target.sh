#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
provision="$script_dir/rootfs/usr/libexec/lucia/lucia-provision-target"
work_dir="$(mktemp -d)"
target_root="$work_dir/target"
target_lucia_root="$work_dir/lucia"
state_dir="$work_dir/state"
trap 'rm -rf "$work_dir"' EXIT

mkdir -p \
    "$target_root/etc/NetworkManager/system-connections" \
    "$target_lucia_root/current/manager" \
    "$state_dir"
printf 'lucia\n' > "$target_root/etc/hostname"
cat > "$target_root/etc/hosts" <<'EOF'
127.0.0.1 localhost
127.0.1.1 lucia
EOF
cat > "$target_root/etc/shadow" <<'EOF'
root:!:20000:0:99999:7:::
lucia-recovery:!:20000:0:99999:7:::
EOF
cat > "$state_dir/provisioning.json" <<'EOF'
{"hostname":"lucia-lab","recoveryPasswordHash":"$6$salt$hashed-password","wifi":{"ssid":"Lab WiFi","passphrase":"lab-wifi-password"}}
EOF
chmod 0600 "$state_dir/provisioning.json"
printf 'old-manager\n' > "$target_lucia_root/current/manager/lucia.ApplianceManager"
printf 'untrimmed-manager\n' > "$work_dir/lucia.ApplianceManager"

LUCIA_MANAGER_OVERRIDE="$work_dir/lucia.ApplianceManager" \
LUCIA_INSTALLER_STATE_DIR="$state_dir" \
LUCIA_TARGET_LUCIA_ROOT="$target_lucia_root" \
LUCIA_TARGET_ROOT="$target_root" \
    "$provision" /dev/disk/by-id/nvme-lab

grep -qx 'lucia-lab' "$target_root/etc/hostname"
grep -qx '127.0.1.1 lucia-lab' "$target_root/etc/hosts"
grep -q '^lucia-recovery:\$6\$salt\$hashed-password:' \
    "$target_root/etc/shadow"
grep -qx 'ssid=Lab WiFi' \
    "$target_root/etc/NetworkManager/system-connections/lucia-home.nmconnection"
grep -qx 'psk=lab-wifi-password' \
    "$target_root/etc/NetworkManager/system-connections/lucia-home.nmconnection"
[[ "$(stat --format '%a' \
    "$target_root/etc/NetworkManager/system-connections/lucia-home.nmconnection")" == "600" ]]
[[ ! -e "$state_dir/provisioning.json" ]]
grep -q '^status=provisioned$' "$state_dir/provision.state"
cmp -s \
    "$work_dir/lucia.ApplianceManager" \
    "$target_lucia_root/current/manager/lucia.ApplianceManager"
[[ "$(stat --format '%a' \
    "$target_lucia_root/current/manager/lucia.ApplianceManager")" == "755" ]]

echo "PASS: target receives recovery, network, and manager configuration"
