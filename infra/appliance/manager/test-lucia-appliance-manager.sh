#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
manager_project="$repo_root/lucia.ApplianceManager/lucia.ApplianceManager.csproj"
work_dir="$(mktemp -d)"
socket_path="$work_dir/appliance-manager.sock"
systemctl_log="$work_dir/systemctl.log"
nmcli_log="$work_dir/nmcli.log"
mountinfo="$work_dir/mountinfo"
sys_block="$work_dir/sys-class-block"
manager_log="$work_dir/manager.log"
manager_pid=""
os_release="$work_dir/os-release"
hostname_file="$work_dir/hostname"
current_release="$work_dir/current"
reboot_required="$work_dir/reboot-required"
telemetry_environment="$work_dir/telemetry.env"
agenthost_environment="$work_dir/lucia.env"
fail_enable="$work_dir/fail-enable"
block_restart="$work_dir/block-restart"
restart_started="$work_dir/restart-started"
restart_completed="$work_dir/restart-completed"
os_version="$work_dir/os-version"
jetson_release="$work_dir/nv_tegra_release"

cleanup() {
    if [[ -n "$manager_pid" ]]; then
        kill "$manager_pid" 2>/dev/null || true
        wait "$manager_pid" 2>/dev/null || true
    fi
    rm -rf "$work_dir"
}
trap cleanup EXIT

cat > "$work_dir/systemctl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_SYSTEMCTL_LOG"
if [[ "$1" == "enable" && -f "$LUCIA_TEST_FAIL_ENABLE_FILE" ]]; then
    printf 'simulated enable failure\n' >&2
    exit 1
fi
if [[ "$*" == "restart lucia-redis.service" \
        && -f "$LUCIA_TEST_BLOCK_RESTART_FILE" ]]; then
    touch "$LUCIA_TEST_RESTART_STARTED_FILE"
    sleep 2
    touch "$LUCIA_TEST_RESTART_COMPLETED_FILE"
fi
if [[ "$1" == "show" ]]; then
    cat <<'STATUS'
Id=lucia-agenthost.service
ActiveState=active
UnitFileState=enabled

Id=lucia-redis.service
ActiveState=active
UnitFileState=enabled

Id=lucia-otelcol.service
ActiveState=inactive
UnitFileState=disabled

Id=lucia-redis-exporter.service
ActiveState=inactive
UnitFileState=disabled
STATUS
fi
EOF
chmod +x "$work_dir/systemctl"
cat > "$work_dir/nmcli" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_NMCLI_LOG"
printf '%s\n' 'yes:Home WiFi:87'
EOF
chmod +x "$work_dir/nmcli"
mkdir -p "$sys_block/nvme1n1"
printf '%s\n' '3906250000' > "$sys_block/nvme1n1/size"
printf '%s\n' \
    '36 25 259:18 / /var/lib/lucia rw,relatime - ext4 /dev/nvme1n1p18 rw' \
    > "$mountinfo"
cat > "$os_release" <<'EOF'
NAME="Ubuntu"
VERSION_ID="22.04"
EOF
printf 'lucia-lab\n' > "$hostname_file"
printf '1.1.0\n' > "$os_version"
printf '# R36 (release), REVISION: 5.2\n' > "$jetson_release"
printf 'Observability__Mode=Off\nPluginDirectory=/var/lib/lucia/plugins\n' \
    > "$agenthost_environment"
mkdir -p "$work_dir/releases/1.2.3"
ln -s releases/1.2.3 "$current_release"

LUCIA_APPLIANCE_SOCKET="$socket_path" \
LUCIA_CURRENT_RELEASE_PATH="$current_release" \
LUCIA_HOSTNAME_PATH="$hostname_file" \
LUCIA_OS_RELEASE_PATH="$os_release" \
LUCIA_OS_VERSION_PATH="$os_version" \
LUCIA_JETSON_RELEASE_PATH="$jetson_release" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_MOUNTINFO_PATH="$mountinfo" \
LUCIA_SYS_BLOCK_PATH="$sys_block" \
LUCIA_REBOOT_REQUIRED_PATH="$reboot_required" \
LUCIA_TELEMETRY_ENV_PATH="$telemetry_environment" \
LUCIA_AGENTHOST_ENV_PATH="$agenthost_environment" \
LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
LUCIA_TEST_SYSTEMCTL_LOG="$systemctl_log" \
LUCIA_TEST_FAIL_ENABLE_FILE="$fail_enable" \
LUCIA_TEST_BLOCK_RESTART_FILE="$block_restart" \
LUCIA_TEST_RESTART_STARTED_FILE="$restart_started" \
LUCIA_TEST_RESTART_COMPLETED_FILE="$restart_completed" \
LUCIA_TEST_NMCLI_LOG="$nmcli_log" \
    dotnet run --no-launch-profile --project "$manager_project" \
    >"$manager_log" 2>&1 &
manager_pid=$!

status=""
for _ in {1..120}; do
    status="$(
        curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
            --unix-socket "$socket_path" \
            http://localhost/v1/status \
            2>/dev/null || true
    )"
    if [[ "$status" == "200" ]]; then
        break
    fi
    if ! kill -0 "$manager_pid" 2>/dev/null; then
        cat "$manager_log" >&2
        exit 1
    fi
    sleep 0.25
done

if [[ "$status" != "200" ]]; then
    cat "$manager_log" >&2
    echo "manager socket did not return appliance status" >&2
    exit 1
fi

[[ "$status" == "200" ]]
grep -q '"hostname":"lucia-lab"' "$work_dir/response.json"
grep -q '"luciaVersion":"1.2.3"' "$work_dir/response.json"
grep -q '"name":"Ubuntu"' "$work_dir/response.json"
grep -q '"versionId":"22.04"' "$work_dir/response.json"
grep -q '"imageVersion":"1.1.0"' "$work_dir/response.json"
grep -q '"jetsonLinuxVersion":"36.5.2"' "$work_dir/response.json"
grep -q '"network":{"ssid":"Home WiFi","signal":87}' "$work_dir/response.json"
grep -q '"storageBytes":2000000000000' "$work_dir/response.json"
grep -q '"rebootRequired":false' "$work_dir/response.json"
grep -q '"id":"agenthost","activeState":"active","unitFileState":"enabled"' \
    "$work_dir/response.json"
grep -q '"id":"collector","activeState":"inactive","unitFileState":"disabled"' \
    "$work_dir/response.json"

echo "PASS: status reports the appliance and allowlisted services"

for service_and_unit in \
    "redis lucia-redis.service"; do
    read -r service unit <<< "$service_and_unit"
    status="$(
        curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
            --unix-socket "$socket_path" \
            --request POST \
            "http://localhost/v1/services/$service/restart"
    )"

    [[ "$status" == "202" ]]
    grep -q "\"service\":\"$service\"" "$work_dir/response.json"
    grep -qx -- "restart $unit" "$systemctl_log"
done

echo "PASS: appliance dependencies restart through fixed systemd units"

touch "$block_restart"
curl --silent --max-time 0.1 --output /dev/null \
    --unix-socket "$socket_path" \
    --request POST \
    http://localhost/v1/services/redis/restart || true
for _ in {1..20}; do
    [[ -e "$restart_started" ]] && break
    sleep 0.05
done
[[ -e "$restart_started" ]]
[[ ! -e "$restart_completed" ]]
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --request POST \
        http://localhost/v1/host/reboot
)"
[[ "$status" == "409" ]]
for _ in {1..50}; do
    [[ -e "$restart_completed" ]] && break
    sleep 0.05
done
[[ -e "$restart_completed" ]]
rm "$block_restart"

echo "PASS: disconnected mutations retain the operation lock until completion"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --request POST \
        http://localhost/v1/host/reboot
)"

[[ "$status" == "202" ]]
grep -q '"status":"reboot-requested"' "$work_dir/response.json"
grep -qx -- '--no-block reboot' "$systemctl_log"

echo "PASS: host reboot uses the fixed systemd operation"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://user:embedded-secret@telemetry.example:4317","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
! grep -q 'embedded-secret' "$work_dir/response.json"
[[ ! -e "$telemetry_environment" ]]

echo "PASS: telemetry rejects credentials embedded in endpoint URLs"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://telemetry.example:4317?token=secret","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
! grep -q 'token=secret' "$work_dir/response.json"
[[ ! -e "$telemetry_environment" ]]

echo "PASS: telemetry rejects endpoint query strings"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"http://telemetry.example:4317","username":"lucia","password":"telemetry-secret","insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
grep -q 'credentials require an HTTPS endpoint' "$work_dir/response.json"
[[ ! -e "$telemetry_environment" ]]

echo "PASS: telemetry rejects credentials over plaintext"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        http://localhost/v1/telemetry
)"

[[ "$status" == "200" ]]
grep -q '"configured":false' "$work_dir/response.json"
grep -q '"hasAuthorization":false' "$work_dir/response.json"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://telemetry.example:4317","username":"lucia","password":"telemetry-secret","insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"

[[ "$status" == "200" ]]
grep -q '"configured":true' "$work_dir/response.json"
grep -q '"enabled":true' "$work_dir/response.json"
grep -q '"endpoint":"https://telemetry.example:4317"' "$work_dir/response.json"
grep -q '"hasAuthorization":true' "$work_dir/response.json"
! grep -q 'telemetry-secret' "$work_dir/response.json"
grep -qx 'OTEL_EXPORTER_OTLP_ENDPOINT=https://telemetry.example:4317' \
    "$telemetry_environment"
grep -qx 'OTEL_EXPORTER_OTLP_INSECURE=false' "$telemetry_environment"
grep -qx 'OTEL_EXPORTER_OTLP_INSECURE_SKIP_VERIFY=false' \
    "$telemetry_environment"
grep -qx 'OTEL_EXPORTER_OTLP_AUTHORIZATION=Basic bHVjaWE6dGVsZW1ldHJ5LXNlY3JldA==' \
    "$telemetry_environment"
grep -qx 'Observability__Mode=Trace' "$agenthost_environment"
grep -qx 'OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317' \
    "$agenthost_environment"
grep -qx 'PluginDirectory=/var/lib/lucia/plugins' "$agenthost_environment"
[[ "$(stat --format '%a' "$telemetry_environment")" == "600" ]]
grep -qx -- 'enable --now lucia-redis-exporter.service lucia-otelcol.service' \
    "$systemctl_log"
grep -qx -- 'restart lucia-agenthost.service' "$systemctl_log"

echo "PASS: telemetry configuration is validated, redacted, and enabled"

cp "$telemetry_environment" "$work_dir/telemetry.https"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"http://telemetry.example:4317","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
cmp -s "$work_dir/telemetry.https" "$telemetry_environment"

echo "PASS: telemetry cannot retain credentials on a plaintext endpoint"

for service_and_unit in \
    "collector lucia-otelcol.service" \
    "redis-exporter lucia-redis-exporter.service"; do
    read -r service unit <<< "$service_and_unit"
    status="$(
        curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
            --unix-socket "$socket_path" \
            --request POST \
            "http://localhost/v1/services/$service/restart"
    )"
    [[ "$status" == "202" ]]
    grep -qx -- "restart $unit" "$systemctl_log"
done

echo "PASS: enabled telemetry services can be restarted"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":false,"endpoint":"https://telemetry.example:4317","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "200" ]]
grep -qx 'Observability__Mode=Off' "$agenthost_environment"
! grep -q '^OTEL_EXPORTER_OTLP_ENDPOINT=' "$agenthost_environment"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --request POST \
        http://localhost/v1/services/collector/restart
)"
[[ "$status" == "409" ]]
grep -q 'Telemetry is disabled' "$work_dir/response.json"

echo "PASS: disabled telemetry services cannot be restarted"

cp "$telemetry_environment" "$work_dir/telemetry.before"
cp "$agenthost_environment" "$work_dir/agenthost.before"
touch "$fail_enable"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://other.example:4317","username":"other","password":"other-secret","insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"

[[ "$status" == "503" ]]
cmp -s "$work_dir/telemetry.before" "$telemetry_environment"
cmp -s "$work_dir/agenthost.before" "$agenthost_environment"
grep -qx -- 'disable --now lucia-otelcol.service lucia-redis-exporter.service' \
    "$systemctl_log"

echo "PASS: failed telemetry enable restores prior configuration and state"
