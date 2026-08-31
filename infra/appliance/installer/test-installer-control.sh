#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
control="$script_dir/rootfs/usr/libexec/lucia/lucia-installer-control"
work_dir="$(mktemp -d)"
loop_device=""

cleanup() {
    if [[ -n "$loop_device" ]]; then
        losetup --detach "$loop_device" 2>/dev/null || true
    fi
    rm -rf "$work_dir"
}
trap cleanup EXIT

mkdir -p "$work_dir/devices" "$work_dir/state"
truncate --size 70G "$work_dir/disk.img"
loop_device="$(losetup --find --show "$work_dir/disk.img")"
ln -s "$loop_device" "$work_dir/devices/nvme-Lab_SSD_LAB123"

LUCIA_DEVICE_DIRECTORY="$work_dir/devices" \
LUCIA_INSTALL_PATH="$script_dir/lucia-install" \
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
    "$control" disks > "$work_dir/non-nvme-disks.json"
grep -qx '\[\]' "$work_dir/non-nvme-disks.json"

echo "PASS: control excludes non-NVMe storage"

LUCIA_ALLOW_LOOP_DEVICES=1 \
LUCIA_DEVICE_DIRECTORY="$work_dir/devices" \
LUCIA_INSTALL_PATH="$script_dir/lucia-install" \
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
    "$control" disks > "$work_dir/disks.json"

python3 - "$work_dir/disks.json" "$work_dir/devices/nvme-Lab_SSD_LAB123" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    disks = json.load(stream)

assert len(disks) == 1
assert disks[0]["id"] == sys.argv[2]
assert disks[0]["sizeBytes"] == 70 * 1024 * 1024 * 1024
assert disks[0]["classification"] == "blank"
assert disks[0]["action"] == "install"
assert disks[0]["confirmationPhrase"].startswith("ERASE ")
PY

echo "PASS: control inventories stable disk identities"

LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"phase":"waiting-for-configuration"' "$work_dir/status.json"

printf '{}\n' > "$work_dir/state/provisioning.json"
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"phase":"waiting-for-configuration"' "$work_dir/status.json"
rm "$work_dir/state/provisioning.json"
printf 'status=approved\n' > "$work_dir/state/erase.authorization"
printf 'status=started\n' > "$work_dir/state/install.requested"
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"phase":"waiting-for-configuration"' "$work_dir/status.json"
rm "$work_dir/state/erase.authorization" "$work_dir/state/install.requested"

echo "PASS: control reports authorization only after both state files commit"

printf 'status=writing\n' > "$work_dir/state/install.state"
printf '%s\n' \
    '{"stage":"writing","bytesWritten":32212254720,"totalBytes":61203283968}' \
    > "$work_dir/state/progress.json"
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"phase":"installing"' "$work_dir/status.json"
grep -q '"stage":"writing"' "$work_dir/status.json"
grep -q '"bytesWritten":32212254720' "$work_dir/status.json"
grep -q '"totalBytes":61203283968' "$work_dir/status.json"
rm "$work_dir/state/install.state"
rm "$work_dir/state/progress.json"

echo "PASS: control reports persistent installation state"

printf '%s\n' '{"stage":"failed"}' > "$work_dir/state/progress.json"
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"phase":"failed"' "$work_dir/status.json"
rm "$work_dir/state/progress.json"

echo "PASS: control reports failed installation state"

cat > "$work_dir/nmcli" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' \
    'Lab WiFi:82:WPA2' \
    'Lab WiFi:41:WPA2' \
    'Guest:63:WPA2 WPA3' \
    'Cafe:75:--' \
    'Enterprise:70:WPA2 802.1X'
EOF
chmod +x "$work_dir/nmcli"

LUCIA_NMCLI_PATH="$work_dir/nmcli" "$control" networks \
    > "$work_dir/networks.json"

python3 - "$work_dir/networks.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    networks = json.load(stream)

assert networks == [
    {"ssid": "Lab WiFi", "signal": 82, "security": "WPA2"},
    {"ssid": "Guest", "signal": 63, "security": "WPA2 WPA3"},
]
PY

echo "PASS: control returns the strongest visible Wi-Fi networks"

printf 'LUCIA-NVME-IMAGE\n' > "$work_dir/payload.img"
truncate --size 1M "$work_dir/payload.img"
sha256sum "$work_dir/payload.img" > "$work_dir/payload.img.sha256"
cat > "$work_dir/systemctl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_SYSTEMCTL_LOG"
if [[ -n "${LUCIA_TEST_FAIL_SYSTEMCTL:-}" \
        && -f "$LUCIA_TEST_FAIL_SYSTEMCTL" ]]; then
    exit 1
fi
EOF
chmod +x "$work_dir/systemctl"

confirmation_phrase="$(
    python3 -c \
        'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))[0]["confirmationPhrase"])' \
        "$work_dir/disks.json"
)"
if printf \
    '{"deviceId":"%s","eraseConfirmation":"%s","hostname":"lucia-lab","recoveryPassword":"correct horse battery staple","wifi":{"ssid":"Lab WiFi","passphrase":"\\ud83d\\ude00password"}}' \
    "$work_dir/devices/nvme-Lab_SSD_LAB123" \
    "$confirmation_phrase" \
    | LUCIA_ALLOW_LOOP_DEVICES=1 \
        LUCIA_CHECKSUM_PATH="$work_dir/payload.img.sha256" \
        LUCIA_DEVICE_DIRECTORY="$work_dir/devices" \
        LUCIA_INSTALL_PATH="$script_dir/lucia-install" \
        LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        LUCIA_NMCLI_PATH="$work_dir/nmcli" \
        LUCIA_PAYLOAD_PATH="$work_dir/payload.img" \
        LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
        LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
        "$control" configure 2>"$work_dir/invalid-wifi.log"; then
    echo "Unicode Wi-Fi passphrase was accepted" >&2
    exit 1
fi
grep -q 'printable ASCII' "$work_dir/invalid-wifi.log"
[[ ! -e "$work_dir/state/erase.authorization" ]]

echo "PASS: control rejects non-ASCII Wi-Fi passphrases"

printf \
    '{"deviceId":"%s","eraseConfirmation":"%s","hostname":"lucia-lab","recoveryPassword":"correct horse battery staple","wifi":{"ssid":"Lab WiFi","passphrase":"lab-wifi-password"}}' \
    "$work_dir/devices/nvme-Lab_SSD_LAB123" \
    "$confirmation_phrase" \
    | LUCIA_ALLOW_LOOP_DEVICES=1 \
        LUCIA_CHECKSUM_PATH="$work_dir/payload.img.sha256" \
        LUCIA_DEVICE_DIRECTORY="$work_dir/devices" \
        LUCIA_INSTALL_PATH="$script_dir/lucia-install" \
        LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        LUCIA_NMCLI_PATH="$work_dir/nmcli" \
        LUCIA_PAYLOAD_PATH="$work_dir/payload.img" \
        LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
        LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
        "$control" configure > "$work_dir/configure.json"

grep -q '"phase":"authorized"' "$work_dir/configure.json"
grep -q '"dashboardKey":"lk_' "$work_dir/configure.json"
grep -q '^status=approved$' "$work_dir/state/erase.authorization"
grep -q '^status=started$' "$work_dir/state/install.requested"
python3 - \
    "$work_dir/state/provisioning.json" \
    "$work_dir/configure.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    provisioning = json.load(stream)
with open(sys.argv[2], encoding="utf-8") as stream:
    response = json.load(stream)

assert provisioning["hostname"] == "lucia-lab"
assert provisioning["recoveryPasswordHash"].startswith("$6$")
assert provisioning["dashboardApiKey"].startswith("lk_")
assert response["dashboardKey"] == provisioning["dashboardApiKey"]
assert provisioning["wifi"] == {
    "ssid": "Lab WiFi",
    "passphrase": "lab-wifi-password",
}
PY
! grep -R -q 'correct horse battery staple' "$work_dir/state"
[[ "$(stat --format '%a' "$work_dir/state/provisioning.json")" == "600" ]]
grep -qx 'start --no-block lucia-firstboot-install.service' "$work_dir/systemctl.log"
[[ "$(stat --format '%a' "$work_dir/state/dashboard-key.handoff")" == "600" ]]
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
python3 - \
    "$work_dir/status.json" \
    "$work_dir/configure.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    status = json.load(stream)
with open(sys.argv[2], encoding="utf-8") as stream:
    response = json.load(stream)

assert status["dashboardKey"] == response["dashboardKey"]
PY
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
    "$control" ack-dashboard-key > "$work_dir/ack.json"
grep -q '"acknowledged":true' "$work_dir/ack.json"
[[ ! -e "$work_dir/state/dashboard-key.handoff" ]]

echo "PASS: control binds approved setup to the selected disk and image"

printf 'status=installed\n' > "$work_dir/state/install.state"
printf '{"stage":"failed"}\n' > "$work_dir/state/progress.json"
LUCIA_INSTALLER_STATE_DIR="$work_dir/state" "$control" status \
    > "$work_dir/status.json"
grep -q '"canRetryNetwork":true' "$work_dir/status.json"
authorization_before="$(sha256sum "$work_dir/state/erase.authorization")"
touch "$work_dir/fail-systemctl"
if printf '{"ssid":"Lab WiFi","passphrase":"corrected-password"}' \
    | LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        LUCIA_NMCLI_PATH="$work_dir/nmcli" \
        LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
        LUCIA_TEST_FAIL_SYSTEMCTL="$work_dir/fail-systemctl" \
        LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
        "$control" retry-network 2>"$work_dir/retry-failure.log"; then
    echo "Network retry succeeded after systemd rejected it" >&2
    exit 1
fi
[[ -e "$work_dir/state/progress.json" ]]
rm "$work_dir/fail-systemctl"
printf '{"ssid":"Lab WiFi","passphrase":"corrected-password"}' \
    | LUCIA_INSTALLER_STATE_DIR="$work_dir/state" \
        LUCIA_NMCLI_PATH="$work_dir/nmcli" \
        LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
        LUCIA_TEST_SYSTEMCTL_LOG="$work_dir/systemctl.log" \
        "$control" retry-network > "$work_dir/retry.json"

grep -q '"phase":"authorized"' "$work_dir/retry.json"
[[ ! -e "$work_dir/state/progress.json" ]]
[[ "$(sha256sum "$work_dir/state/erase.authorization")" == "$authorization_before" ]]
grep -q '"passphrase":"corrected-password"' \
    "$work_dir/state/provisioning.json"

echo "PASS: installed images accept credentials-only network retries"
