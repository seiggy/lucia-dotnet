#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bootstrap="$script_dir/rootfs/usr/libexec/lucia/lucia-network-bootstrap"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cat > "$work_dir/bootstrap.env" <<'EOF'
LUCIA_SETUP_SSID=Lucia-LAB123
LUCIA_WIFI_INTERFACE=wlan0
EOF
chmod 0600 "$work_dir/bootstrap.env"
cat > "$work_dir/nmcli" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_NMCLI_LOG"
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
set -euo pipefail
if [[ "$1" == "-C" ]]; then
    grep -Fqx -- "-I ${*:2:1} 1 ${*:3}" "$LUCIA_TEST_IPTABLES_LOG" \
        2>/dev/null
    exit
fi
printf '%s\n' "$*" >> "$LUCIA_TEST_IPTABLES_LOG"
EOF
chmod +x "$work_dir/nmcli" "$work_dir/iptables"

LUCIA_BOOTSTRAP_ENV="$work_dir/bootstrap.env" \
LUCIA_CONNECTION_PATH="$work_dir/lucia-setup.nmconnection" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
LUCIA_TEST_NMCLI_LOG="$work_dir/nmcli.log" \
LUCIA_TEST_IPTABLES_LOG="$work_dir/iptables.log" \
    "$bootstrap"
LUCIA_BOOTSTRAP_ENV="$work_dir/bootstrap.env" \
LUCIA_CONNECTION_PATH="$work_dir/lucia-setup.nmconnection" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
LUCIA_TEST_NMCLI_LOG="$work_dir/nmcli.log" \
LUCIA_TEST_IPTABLES_LOG="$work_dir/iptables.log" \
    "$bootstrap"

grep -qx 'mode=ap' "$work_dir/lucia-setup.nmconnection"
grep -qx 'ap-isolation=1' "$work_dir/lucia-setup.nmconnection"
! grep -qx '^\[wifi-security\]$' "$work_dir/lucia-setup.nmconnection"
grep -qx 'method=shared' "$work_dir/lucia-setup.nmconnection"
[[ "$(stat --format '%a' "$work_dir/lucia-setup.nmconnection")" == "600" ]]
grep -q 'connection load .*lucia-setup.nmconnection' "$work_dir/nmcli.log"
grep -q 'radio wifi on' "$work_dir/nmcli.log"
[[ "$(grep -c 'connection up lucia-setup' "$work_dir/nmcli.log")" == "1" ]]
[[ "$(grep -c -- '-I FORWARD 1 -i wlan0 -j DROP' "$work_dir/iptables.log")" == "1" ]]

echo "PASS: network bootstrap creates an open isolated setup network"

printf '%s\n' '# derive setup identity from device serial' > "$work_dir/derived.env"
chmod 0600 "$work_dir/derived.env"
printf 'JETSONABC123\n' > "$work_dir/serial"
rm "$work_dir/nmcli.log" "$work_dir/iptables.log"
LUCIA_BOOTSTRAP_ENV="$work_dir/derived.env" \
LUCIA_CONNECTION_PATH="$work_dir/derived.nmconnection" \
LUCIA_DEVICE_SERIAL_PATH="$work_dir/serial" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
LUCIA_TEST_NMCLI_LOG="$work_dir/nmcli.log" \
LUCIA_TEST_IPTABLES_LOG="$work_dir/iptables.log" \
    "$bootstrap"
grep -qx 'ssid=Lucia-ABC123' "$work_dir/derived.nmconnection"
! grep -qx '^\[wifi-security\]$' "$work_dir/derived.nmconnection"

echo "PASS: headless setup identity derives its discoverable SSID from the Jetson"

printf 'JETSONXYZ789\n' > "$work_dir/serial"
LUCIA_BOOTSTRAP_ENV="$work_dir/derived.env" \
LUCIA_CONNECTION_PATH="$work_dir/derived.nmconnection" \
LUCIA_DEVICE_SERIAL_PATH="$work_dir/serial" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_IPTABLES_PATH="$work_dir/iptables" \
LUCIA_TEST_NMCLI_LOG="$work_dir/nmcli.log" \
LUCIA_TEST_IPTABLES_LOG="$work_dir/iptables.log" \
    "$bootstrap"
grep -qx 'ssid=Lucia-XYZ789' "$work_dir/derived.nmconnection"
[[ "$(grep -c 'connection up lucia-setup' "$work_dir/nmcli.log")" == "2" ]]

echo "PASS: reused media refreshes setup identity for each Jetson"
