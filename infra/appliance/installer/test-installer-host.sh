#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
installer_project="$repo_root/lucia.InstallerHost/lucia.InstallerHost.csproj"
work_dir="$(mktemp -d)"
installer_log="$work_dir/installer.log"
control_log="$work_dir/control.log"
control_input="$work_dir/control-input.json"
installer_pid=""
base_url="http://127.0.0.1:18098"
claim_path="$work_dir/claim.sha256"
cookie_jar="$work_dir/cookies.txt"

cat > "$work_dir/lucia-installer-control" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_CONTROL_LOG"
case "$1" in
    configure)
        cat > "$LUCIA_TEST_CONTROL_INPUT"
        printf '%s\n' '{"phase":"authorized"}'
        ;;
    disks)
        printf '%s\n' '[{"id":"/dev/disk/by-id/nvme-lab","model":"Lab NVMe","serial":"LAB123","confirmationPhrase":"ERASE LAB123","sizeBytes":2000000000000,"classification":"occupied","action":"confirmation-required"}]'
        ;;
    networks)
        printf '%s\n' '[{"ssid":"Lab WiFi","signal":82,"security":"WPA2"}]'
        ;;
    status)
        printf '%s\n' '{"phase":"waiting-for-configuration"}'
        ;;
    *)
        exit 64
        ;;
esac
EOF
chmod +x "$work_dir/lucia-installer-control"

cleanup() {
    if [[ -n "$installer_pid" ]]; then
        kill "$installer_pid" 2>/dev/null || true
        wait "$installer_pid" 2>/dev/null || true
    fi
    rm -rf "$work_dir"
}
trap cleanup EXIT

Appliance__Mode=Installer \
Appliance__ControlPath="$work_dir/lucia-installer-control" \
Appliance__ControlCommand="" \
Appliance__ClaimPath="$claim_path" \
LUCIA_TEST_CONTROL_LOG="$control_log" \
LUCIA_TEST_CONTROL_INPUT="$control_input" \
ASPNETCORE_URLS="$base_url" \
    dotnet run --no-launch-profile --project "$installer_project" \
    >"$installer_log" 2>&1 &
installer_pid=$!

for _ in {1..120}; do
    status="$(
        curl --silent --output /dev/null --write-out '%{http_code}' \
            "$base_url/api/installer/status" 2>/dev/null || true
    )"
    if [[ "$status" == "401" ]]; then
        break
    fi
    if ! kill -0 "$installer_pid" 2>/dev/null; then
        cat "$installer_log" >&2
        exit 1
    fi
    sleep 0.25
done

[[ "$status" == "401" ]]

status="$(
    curl --silent --output "$work_dir/capabilities.json" --write-out '%{http_code}' \
        "$base_url/api/installer/capabilities"
)"
[[ "$status" == "200" ]]
grep -q '"mode":"installer"' "$work_dir/capabilities.json"
grep -q '"requiresSetupCode":false' "$work_dir/capabilities.json"

echo "PASS: installer mode is discoverable without exposing setup data"

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        "$base_url/generate_204"
)"
[[ "$status" == "302" ]]

echo "PASS: captive network probes redirect to setup"

status="$(
    curl --silent --cookie-jar "$cookie_jar" \
        --output "$work_dir/claim.json" --write-out '%{http_code}' \
        --request POST \
        "$base_url/api/installer/claim"
)"
[[ "$status" == "200" ]]
grep -q '"claimed":true' "$work_dir/claim.json"
[[ -s "$claim_path" ]]
[[ "$(stat --format '%a' "$claim_path")" == "600" ]]

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        --request POST \
        "$base_url/api/installer/claim"
)"
[[ "$status" == "409" ]]

echo "PASS: first browser atomically claims the installer"

status="$(
    curl --silent --output "$work_dir/status.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        "$base_url/api/installer/status"
)"

[[ "$status" == "200" ]]
grep -q '"phase":"waiting-for-configuration"' "$work_dir/status.json"

echo "PASS: installer status requires the claiming browser"

status="$(
    curl --silent --output "$work_dir/disks.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        "$base_url/api/installer/disks"
)"

[[ "$status" == "200" ]]
grep -q '"id":"/dev/disk/by-id/nvme-lab"' "$work_dir/disks.json"
grep -q '"classification":"occupied"' "$work_dir/disks.json"
grep -qx 'disks' "$control_log"

echo "PASS: installer lists storage through the control interface"

status="$(
    curl --silent --output "$work_dir/networks.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        "$base_url/api/installer/networks"
)"

[[ "$status" == "200" ]]
grep -q '"ssid":"Lab WiFi"' "$work_dir/networks.json"
grep -qx 'networks' "$control_log"

echo "PASS: installer lists Wi-Fi networks through the control interface"

status="$(
    curl --silent --output "$work_dir/configure.json" --write-out '%{http_code}' \
        --header "Content-Type: application/json" \
        --cookie "$cookie_jar" \
        --request POST \
        --data '{"deviceId":"/dev/disk/by-id/nvme-lab","eraseConfirmation":"ERASE LAB123","hostname":"lucia-lab","recoveryPassword":"correct horse battery staple","wifi":{"ssid":"Lab WiFi","passphrase":"lab-wifi-password"}}' \
        "$base_url/api/installer/install"
)"

[[ "$status" == "202" ]]
grep -q '"phase":"authorized"' "$work_dir/configure.json"
grep -qx 'configure' "$control_log"
grep -q '"recoveryPassword":"correct horse battery staple"' "$control_input"

echo "PASS: installer sends approved setup to control over standard input"
