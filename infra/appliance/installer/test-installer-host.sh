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
canonical_host="lucia.setup"
canonical_origin="http://lucia.setup"
claim_path="$work_dir/claim.sha256"
dashboard_key_path="$work_dir/dashboard-key.handoff"
cookie_jar="$work_dir/cookies.txt"
block_control="$work_dir/block-control"
control_started="$work_dir/control-started"

cat > "$work_dir/lucia-installer-control" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_CONTROL_LOG"
case "$1" in
    configure)
        cat > "$LUCIA_TEST_CONTROL_INPUT"
        if grep -q '"deviceId":"/gone"' "$LUCIA_TEST_CONTROL_INPUT"; then
            printf '%s\n' \
                '{"error":"Selected storage is no longer available."}' >&2
            exit 2
        fi
        printf '%s\n' 'lk_host-bootstrap-key' \
            > "$LUCIA_TEST_DASHBOARD_KEY_PATH"
        chmod 0600 "$LUCIA_TEST_DASHBOARD_KEY_PATH"
        printf '%s\n' \
            '{"phase":"authorized","dashboardKey":"lk_host-bootstrap-key"}'
        ;;
    ack-dashboard-key)
        rm -f "$LUCIA_TEST_DASHBOARD_KEY_PATH"
        printf '%s\n' '{"acknowledged":true}'
        ;;
    retry-network)
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
        if [[ -f "$LUCIA_TEST_BLOCK_CONTROL" ]]; then
            printf '%s\n' "$$" > "$LUCIA_TEST_CONTROL_STARTED"
            sleep 30
        fi
        if [[ -f "$LUCIA_TEST_DASHBOARD_KEY_PATH" ]]; then
            printf '%s\n' \
                '{"phase":"authorized","dashboardKey":"lk_host-bootstrap-key"}'
        else
            printf '%s\n' '{"phase":"waiting-for-configuration"}'
        fi
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
LUCIA_TEST_BLOCK_CONTROL="$block_control" \
LUCIA_TEST_CONTROL_STARTED="$control_started" \
LUCIA_TEST_DASHBOARD_KEY_PATH="$dashboard_key_path" \
ASPNETCORE_URLS="$base_url" \
    dotnet run --no-launch-profile --project "$installer_project" \
    >"$installer_log" 2>&1 &
installer_pid=$!

for _ in {1..120}; do
    status="$(
        curl --silent --output /dev/null --write-out '%{http_code}' \
            --header "Host: $canonical_host" \
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
        --header "Host: $canonical_host" \
        "$base_url/api/installer/capabilities"
)"
[[ "$status" == "200" ]]
grep -q '"mode":"installer"' "$work_dir/capabilities.json"
grep -q '"requiresSetupCode":false' "$work_dir/capabilities.json"

echo "PASS: installer mode is discoverable without exposing setup data"

status="$(
    curl --silent --dump-header "$work_dir/captive.headers" \
        --output /dev/null --write-out '%{http_code}' \
        "$base_url/generate_204"
)"
[[ "$status" == "302" ]]
grep -qi "^Location: $canonical_origin/install" "$work_dir/captive.headers"

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        "$base_url/api/installer/capabilities"
)"
[[ "$status" == "400" ]]

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        --header "Host: $canonical_host" \
        --header "Origin: http://attacker.example" \
        --request POST \
        "$base_url/api/installer/claim"
)"
[[ "$status" == "403" ]]
[[ ! -e "$claim_path" ]]

echo "PASS: captive requests move to one origin before API access"

status="$(
    curl --silent --dump-header "$work_dir/claim.headers" \
        --cookie-jar "$cookie_jar" \
        --output "$work_dir/claim.json" --write-out '%{http_code}' \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --request POST \
        "$base_url/api/installer/claim"
)"
[[ "$status" == "200" ]]
grep -q '"claimed":true' "$work_dir/claim.json"
grep -qi 'Set-Cookie: lucia-installer-session=.*Max-Age=86400' \
    "$work_dir/claim.headers"
[[ -s "$claim_path" ]]
[[ "$(stat --format '%a' "$claim_path")" == "600" ]]

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --request POST \
        "$base_url/api/installer/claim"
)"
[[ "$status" == "409" ]]

echo "PASS: first browser atomically claims the installer"

touch "$block_control"
curl --silent --max-time 0.1 --output /dev/null \
    --cookie "$cookie_jar" \
    --header "Host: $canonical_host" \
    "$base_url/api/installer/status" || true
control_pid="$(cat "$control_started")"
control_stopped=false
for _ in {1..40}; do
    if ! kill -0 "$control_pid" 2>/dev/null; then
        control_stopped=true
        break
    fi
    sleep 0.05
done
[[ "$control_stopped" == true ]]
rm "$block_control"

echo "PASS: canceled installer requests terminate control helpers"

status="$(
    curl --silent --dump-header "$work_dir/status.headers" \
        --output "$work_dir/status.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --header "Host: $canonical_host" \
        "$base_url/api/installer/status"
)"

[[ "$status" == "200" ]]
grep -qi '^Cache-Control: no-store' "$work_dir/status.headers"
grep -q '"phase":"waiting-for-configuration"' "$work_dir/status.json"

echo "PASS: installer status requires the claiming browser"

status="$(
    curl --silent --output "$work_dir/disks.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --header "Host: $canonical_host" \
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
        --header "Host: $canonical_host" \
        "$base_url/api/installer/networks"
)"

[[ "$status" == "200" ]]
grep -q '"ssid":"Lab WiFi"' "$work_dir/networks.json"
grep -qx 'networks' "$control_log"

echo "PASS: installer lists Wi-Fi networks through the control interface"

status="$(
    curl --silent --output "$work_dir/configure-error.json" --write-out '%{http_code}' \
        --header "Content-Type: application/json" \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --cookie "$cookie_jar" \
        --request POST \
        --data '{"deviceId":"/gone","eraseConfirmation":"ERASE GONE","hostname":"lucia-lab","recoveryPassword":"correct horse battery staple","wifi":null}' \
        "$base_url/api/installer/install"
)"
[[ "$status" == "400" ]]
grep -q 'Selected storage is no longer available.' \
    "$work_dir/configure-error.json"

echo "PASS: installer returns sanitized control validation errors"

status="$(
    curl --silent --output "$work_dir/configure.json" --write-out '%{http_code}' \
        --header "Content-Type: application/json" \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --cookie "$cookie_jar" \
        --request POST \
        --data '{"deviceId":"/dev/disk/by-id/nvme-lab","eraseConfirmation":"ERASE LAB123","hostname":"lucia-lab","recoveryPassword":"correct horse battery staple","wifi":{"ssid":"Lab WiFi","passphrase":"lab-wifi-password"}}' \
        "$base_url/api/installer/install"
)"

[[ "$status" == "202" ]]
grep -q '"phase":"authorized"' "$work_dir/configure.json"
grep -q '"dashboardKey":"lk_host-bootstrap-key"' "$work_dir/configure.json"
[[ -s "$dashboard_key_path" ]]
[[ "$(stat --format '%a' "$dashboard_key_path")" == "600" ]]
grep -qx 'configure' "$control_log"
grep -q '"recoveryPassword":"correct horse battery staple"' "$control_input"

echo "PASS: installer sends approved setup to control over standard input"

status="$(
    curl --silent --output "$work_dir/status.json" --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --header "Host: $canonical_host" \
        "$base_url/api/installer/status"
)"
[[ "$status" == "200" ]]
grep -q '"dashboardKey":"lk_host-bootstrap-key"' "$work_dir/status.json"

status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --request POST \
        "$base_url/api/installer/dashboard-key/acknowledge"
)"
[[ "$status" == "204" ]]
[[ ! -e "$dashboard_key_path" ]]

curl --silent --output "$work_dir/status.json" \
    --cookie "$cookie_jar" \
    --header "Host: $canonical_host" \
    "$base_url/api/installer/status"
! grep -q '"dashboardKey"' "$work_dir/status.json"

echo "PASS: dashboard key handoff survives until browser acknowledgment"

status="$(
    curl --silent --output "$work_dir/retry.json" --write-out '%{http_code}' \
        --header "Content-Type: application/json" \
        --header "Host: $canonical_host" \
        --header "Origin: $canonical_origin" \
        --cookie "$cookie_jar" \
        --request POST \
        --data '{"ssid":"Lab WiFi","passphrase":"corrected-password"}' \
        "$base_url/api/installer/retry-network"
)"
[[ "$status" == "202" ]]
grep -q '"phase":"authorized"' "$work_dir/retry.json"
grep -qx 'retry-network' "$control_log"
grep -q '"passphrase":"corrected-password"' "$control_input"

echo "PASS: installer forwards credentials-only network retries"
