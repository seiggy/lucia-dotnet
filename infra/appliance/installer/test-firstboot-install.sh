#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
firstboot="$script_dir/rootfs/usr/libexec/lucia/lucia-firstboot-install"
work_dir="$(mktemp -d)"
loop_device=""
device_id="/dev/disk/by-id/lucia-test-$$"

cleanup() {
    if [[ -n "$loop_device" ]]; then
        losetup --detach "$loop_device" 2>/dev/null || true
    fi
    rm -f "$device_id"
    rm -rf "$work_dir"
}
trap cleanup EXIT

truncate --size 70G "$work_dir/disk.img"
loop_device="$(losetup --find --show "$work_dir/disk.img")"
mkdir -p /dev/disk/by-id
ln -s "$loop_device" "$device_id"
printf 'payload\n' > "$work_dir/payload.img"
sha256sum "$work_dir/payload.img" > "$work_dir/payload.img.sha256"
mkdir -p "$work_dir/state"
cat > "$work_dir/state/erase.authorization" <<EOF
status=approved
device=$device_id
EOF

cat > "$work_dir/lucia-install" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" > "$LUCIA_TEST_INSTALL_LOG"
state_file=""
while [[ $# -gt 0 ]]; do
    if [[ "$1" == "--state-file" ]]; then
        state_file="$2"
        break
    fi
    shift
done
printf 'status=installed\n' > "$state_file"
EOF
cat > "$work_dir/lucia-provision-target" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" > "$LUCIA_TEST_PROVISION_LOG"
rm "$LUCIA_INSTALLER_STATE_DIR/provisioning.json"
printf 'status=provisioned\n' > "$LUCIA_INSTALLER_STATE_DIR/provision.state"
EOF
cat > "$work_dir/lucia-expand-data" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" > "$LUCIA_TEST_EXPAND_LOG"
EOF
cat > "$work_dir/systemctl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_SYSTEMCTL_LOG"
EOF
chmod +x \
    "$work_dir/lucia-install" \
    "$work_dir/lucia-expand-data" \
    "$work_dir/lucia-provision-target" \
    "$work_dir/systemctl"

printf '{}\n' > "$work_dir/state/provisioning.json"
output="$(
    LUCIA_CHECKSUM_PATH="$work_dir/payload.img.sha256" \
    LUCIA_INSTALL_PATH="$work_dir/lucia-install" \
    LUCIA_EXPAND_PATH="$work_dir/lucia-expand-data" \
    LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
    LUCIA_PAYLOAD_PATH="$work_dir/payload.img" \
    LUCIA_PROVISION_PATH="$work_dir/lucia-provision-target" \
    LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
    LUCIA_TEST_INSTALL_LOG="$work_dir/install.log" \
    LUCIA_TEST_PROVISION_LOG="$work_dir/provision.log" \
    LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
        "$firstboot"
)"
grep -q 'Waiting for storage selection and erase authorization.' <<< "$output"
[[ ! -e "$work_dir/install.log" ]]

printf 'status=started\n' > "$work_dir/state/install.requested"
LUCIA_CHECKSUM_PATH="$work_dir/payload.img.sha256" \
LUCIA_INSTALL_PATH="$work_dir/lucia-install" \
LUCIA_EXPAND_PATH="$work_dir/lucia-expand-data" \
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
LUCIA_PAYLOAD_PATH="$work_dir/payload.img" \
LUCIA_PROVISION_PATH="$work_dir/lucia-provision-target" \
LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
LUCIA_TEST_INSTALL_LOG="$work_dir/install.log" \
LUCIA_TEST_EXPAND_LOG="$work_dir/expand.log" \
LUCIA_TEST_PROVISION_LOG="$work_dir/provision.log" \
LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
    "$firstboot"

grep -q -- "--device $device_id" "$work_dir/install.log"
grep -qx -- "$device_id" "$work_dir/expand.log"
grep -qx -- "$device_id" "$work_dir/provision.log"
grep -qx -- '--no-block poweroff' "$work_dir/systemctl.log"
grep -q '^status=provisioned$' "$work_dir/state/provision.state"
grep -q '"stage":"powering-off"' "$work_dir/state/progress.json"

echo "PASS: first boot installs, provisions, and powers off"
