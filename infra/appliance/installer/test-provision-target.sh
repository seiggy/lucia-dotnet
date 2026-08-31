#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
provision="$script_dir/rootfs/usr/libexec/lucia/lucia-provision-target"
work_dir="$(mktemp -d)"
target_root="$work_dir/target"
target_lucia_root="$work_dir/lucia"
state_dir="$work_dir/state"
nmcli_log="$work_dir/nmcli.log"
fail_checkpoint="$work_dir/fail-checkpoint"
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
cat > "$work_dir/nmcli" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_NMCLI_LOG"
if [[ "$*" == "--terse --fields DEVICE,TYPE device status" ]]; then
    printf 'wlan0:wifi\n'
elif [[ "$1 $2" == "device checkpoint" \
        && -f "$LUCIA_TEST_FAIL_CHECKPOINT" ]]; then
    exit 1
fi
EOF
chmod +x "$work_dir/nmcli"

LUCIA_MANAGER_OVERRIDE="$work_dir/lucia.ApplianceManager" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_INSTALLER_STATE_DIR="$state_dir" \
LUCIA_TEST_FAIL_CHECKPOINT="$fail_checkpoint" \
LUCIA_TEST_NMCLI_LOG="$nmcli_log" \
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
grep -q '^hostname=lucia-lab$' "$state_dir/provision.state"
cmp -s \
    "$work_dir/lucia.ApplianceManager" \
    "$target_lucia_root/current/manager/lucia.ApplianceManager"
[[ "$(stat --format '%a' \
    "$target_lucia_root/current/manager/lucia.ApplianceManager")" == "755" ]]
grep -q '^device checkpoint --timeout 45 wlan0 -- .* --wait 30 connection up id lucia-home ifname wlan0$' \
    "$nmcli_log"
grep -q '^connection delete id lucia-home$' "$nmcli_log"
grep -q '^--wait 30 connection up id lucia-setup ifname wlan0$' "$nmcli_log"

echo "PASS: target receives recovery, network, and manager configuration"

cat > "$state_dir/provisioning.json" <<'EOF'
{"hostname":"lucia-lab","recoveryPasswordHash":"$6$salt$hashed-password","wifi":{"ssid":"Lab WiFi","passphrase":"wrong-password"}}
EOF
rm -f "$state_dir/provision.state"
touch "$fail_checkpoint"
if LUCIA_MANAGER_OVERRIDE="$work_dir/lucia.ApplianceManager" \
    LUCIA_NMCLI_PATH="$work_dir/nmcli" \
    LUCIA_INSTALLER_STATE_DIR="$state_dir" \
    LUCIA_TEST_FAIL_CHECKPOINT="$fail_checkpoint" \
    LUCIA_TEST_NMCLI_LOG="$nmcli_log" \
    LUCIA_TARGET_LUCIA_ROOT="$target_lucia_root" \
    LUCIA_TARGET_ROOT="$target_root" \
        "$provision" /dev/disk/by-id/nvme-lab \
        2>"$work_dir/checkpoint-failure.log"; then
    echo "Failed Wi-Fi checkpoint completed provisioning" >&2
    exit 1
fi
[[ -e "$state_dir/provisioning.json" ]]
[[ ! -e "$state_dir/provision.state" ]]
[[ "$(grep -c '^--wait 30 connection up id lucia-setup ifname wlan0$' \
    "$nmcli_log")" -eq 2 ]]

echo "PASS: failed Wi-Fi checkpoint keeps setup ready for retry"
