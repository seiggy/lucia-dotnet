#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
manager_project="$repo_root/lucia.ApplianceManager/lucia.ApplianceManager.csproj"
manager_health="$repo_root/infra/appliance/rootfs/usr/libexec/lucia/lucia-manager-health-check"
work_dir="$(mktemp -d)"
socket_path="$work_dir/appliance-manager.sock"
systemctl_log="$work_dir/systemctl.log"
nmcli_log="$work_dir/nmcli.log"
network_mode="$work_dir/network-mode"
nmcli_block="$work_dir/nmcli-block"
nmcli_started="$work_dir/nmcli-started"
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
fail_collector_restart="$work_dir/fail-collector-restart"
block_restart="$work_dir/block-restart"
restart_started="$work_dir/restart-started"
restart_completed="$work_dir/restart-completed"
update_log="$work_dir/update.log"
os_version="$work_dir/os-version"
jetson_release="$work_dir/nv_tegra_release"
device_tree_compatible="$work_dir/device-tree-compatible"

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
if [[ "$*" == "restart lucia-otelcol.service" \
        && -f "$LUCIA_TEST_FAIL_COLLECTOR_RESTART_FILE" ]]; then
    rm "$LUCIA_TEST_FAIL_COLLECTOR_RESTART_FILE"
    printf 'simulated collector restart failure\n' >&2
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
mode="$(cat "$LUCIA_TEST_NETWORK_MODE")"
if [[ "$*" == *"IN-USE,SSID,SIGNAL device wifi list"* ]]; then
    if [[ -f "$LUCIA_TEST_NMCLI_BLOCK" ]]; then
        printf '%s\n' "$$" > "$LUCIA_TEST_NMCLI_STARTED"
        sleep 30
    fi
    [[ "$mode" != "wifi" ]] || printf '%s\n' 'yes:Home WiFi:87'
elif [[ "$*" == *"DEVICE,TYPE,STATE device status"* \
        && "$mode" == "ethernet" ]]; then
    printf '%s\n' 'eth0:ethernet:connected'
fi
EOF
chmod +x "$work_dir/nmcli"
cat > "$work_dir/lucia-update" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_UPDATE_LOG"
if [[ "$1" == "apply" && "$2" == "os" ]]; then
    mkdir -p "$LUCIA_UPDATE_ROOT/state"
    printf 'operation_id=%s\nstatus=pending\n' \
        "$LUCIA_UPDATE_OPERATION_ID" \
        > "$LUCIA_UPDATE_ROOT/state/os.env"
fi
EOF
chmod +x "$work_dir/lucia-update"
printf 'wifi\n' > "$network_mode"
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
printf 'nvidia,p3768-0000+p3767-0005\0nvidia,tegra234\0' \
    > "$device_tree_compatible"
printf 'Observability__Mode=Off\nPluginDirectory=/var/lib/lucia/plugins\n' \
    > "$agenthost_environment"
mkdir -p "$work_dir/releases/1.2.3"
ln -s releases/1.2.3 "$current_release"
mkdir -p "$work_dir/updates/state"
printf 'status=writing\n' > "$work_dir/updates/state/os.env"
cat > "$work_dir/updates/state/operation.json" <<'EOF'
{"Action":"apply","Channel":"os","Status":"running","Tag":"v1.4.0","Message":null}
EOF
printf 'phase=switched\n' > "$work_dir/updates/state/lucia.env"

manager_command=(dotnet run --no-launch-profile --project "$manager_project")
manager_validation_command=("${manager_command[@]}" --)
if [[ -n "${LUCIA_MANAGER_BINARY:-}" ]]; then
    manager_command=("$LUCIA_MANAGER_BINARY")
    manager_validation_command=("${manager_command[@]}")
fi
if ! timeout 30 "${manager_validation_command[@]}" --validate; then
    echo "Manager validation mode failed" >&2
    exit 1
fi

LUCIA_APPLIANCE_SOCKET="$socket_path" \
LUCIA_CURRENT_RELEASE_PATH="$current_release" \
LUCIA_HOSTNAME_PATH="$hostname_file" \
LUCIA_OS_RELEASE_PATH="$os_release" \
LUCIA_OS_VERSION_PATH="$os_version" \
LUCIA_JETSON_RELEASE_PATH="$jetson_release" \
LUCIA_DEVICE_TREE_COMPATIBLE_PATH="$device_tree_compatible" \
LUCIA_NMCLI_PATH="$work_dir/nmcli" \
LUCIA_MOUNTINFO_PATH="$mountinfo" \
LUCIA_SYS_BLOCK_PATH="$sys_block" \
LUCIA_REBOOT_REQUIRED_PATH="$reboot_required" \
LUCIA_TELEMETRY_ENV_PATH="$telemetry_environment" \
LUCIA_AGENTHOST_ENV_PATH="$agenthost_environment" \
LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
LUCIA_UPDATE_PATH="$work_dir/lucia-update" \
LUCIA_UPDATE_ROOT="$work_dir/updates" \
LUCIA_TEST_SYSTEMCTL_LOG="$systemctl_log" \
LUCIA_TEST_FAIL_ENABLE_FILE="$fail_enable" \
LUCIA_TEST_FAIL_COLLECTOR_RESTART_FILE="$fail_collector_restart" \
LUCIA_TEST_BLOCK_RESTART_FILE="$block_restart" \
LUCIA_TEST_RESTART_STARTED_FILE="$restart_started" \
LUCIA_TEST_RESTART_COMPLETED_FILE="$restart_completed" \
LUCIA_TEST_UPDATE_LOG="$update_log" \
LUCIA_TEST_NMCLI_LOG="$nmcli_log" \
LUCIA_TEST_NETWORK_MODE="$network_mode" \
LUCIA_TEST_NMCLI_BLOCK="$nmcli_block" \
LUCIA_TEST_NMCLI_STARTED="$nmcli_started" \
    "${manager_command[@]}" \
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
grep -q '"board":"jetson-orin-nano-super-p3767-0005"' \
    "$work_dir/response.json"
grep -q '"network":{"ssid":"Home WiFi","signal":87}' "$work_dir/response.json"
grep -q '"storageBytes":2000000000000' "$work_dir/response.json"
grep -q '"rebootRequired":false' "$work_dir/response.json"
grep -q '"id":"agenthost","activeState":"active","unitFileState":"enabled"' \
    "$work_dir/response.json"
grep -q '"id":"collector","activeState":"inactive","unitFileState":"disabled"' \
    "$work_dir/response.json"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/updates/operation
grep -q '"status":"failed"' "$work_dir/response.json"
grep -qx 'status=failed' "$work_dir/updates/state/os.env"
grep -qx 'recover lucia' "$update_log"
[[ "$(stat --format '%a' "$work_dir/updates/state")" == "750" ]]
[[ "$(stat --format '%a' "$work_dir/updates/state/operation.json")" == "600" ]]

echo "PASS: interrupted inactive-slot writes recover to a retryable state"

LUCIA_APPLIANCE_SOCKET="$socket_path" \
LUCIA_CURL_PATH="$(command -v curl)" \
LUCIA_UPDATE_PATH="$work_dir/lucia-update" \
LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
LUCIA_TEST_UPDATE_LOG="$update_log" \
LUCIA_TEST_SYSTEMCTL_LOG="$systemctl_log" \
LUCIA_MANAGER_HEALTH_ATTEMPTS=1 \
LUCIA_MANAGER_HEALTH_DELAY_SECONDS=0 \
    bash <(sed 's/\r$//' "$manager_health")
grep -qx 'finalize lucia' "$update_log"
cat > "$work_dir/curl-failure" <<'EOF'
#!/usr/bin/env bash
exit 1
EOF
chmod +x "$work_dir/curl-failure"
if LUCIA_APPLIANCE_SOCKET="$socket_path" \
        LUCIA_CURL_PATH="$work_dir/curl-failure" \
        LUCIA_UPDATE_PATH="$work_dir/lucia-update" \
        LUCIA_SYSTEMCTL_PATH="$work_dir/systemctl" \
        LUCIA_TEST_UPDATE_LOG="$update_log" \
        LUCIA_TEST_SYSTEMCTL_LOG="$systemctl_log" \
        LUCIA_MANAGER_HEALTH_ATTEMPTS=1 \
        LUCIA_MANAGER_HEALTH_DELAY_SECONDS=0 \
        bash <(sed 's/\r$//' "$manager_health"); then
    echo "Failed manager health was accepted" >&2
    exit 1
fi
grep -qx 'recover lucia' "$update_log"
grep -qx 'stop lucia-agenthost.service lucia-redis.service' "$systemctl_log"
: > "$update_log"

echo "PASS: manager startup finalizes or recovers pending Lucia updates"

echo "PASS: status reports the appliance and allowlisted services"

printf 'ethernet\n' > "$network_mode"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/status
grep -q '"network":{"ssid":"Ethernet","signal":null}' \
    "$work_dir/response.json"
printf 'unavailable\n' > "$network_mode"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/status
grep -q '"network":{"ssid":"Unavailable","signal":null}' \
    "$work_dir/response.json"
printf 'wifi\n' > "$network_mode"

echo "PASS: status distinguishes wired and unavailable networking"

touch "$nmcli_block"
curl --silent --max-time 0.1 --output /dev/null \
    --unix-socket "$socket_path" \
    http://localhost/v1/status || true
nmcli_pid="$(cat "$nmcli_started")"
nmcli_stopped=false
for _ in {1..40}; do
    if ! kill -0 "$nmcli_pid" 2>/dev/null; then
        nmcli_stopped=true
        break
    fi
    sleep 0.05
done
[[ "$nmcli_stopped" == true ]]
rm "$nmcli_block"

echo "PASS: canceled status requests terminate nmcli"

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

mkdir "$work_dir/updates/state/operation.json.tmp"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":"v1.4.0","operationId":"00000000-0000-0000-0000-000000000000"}' \
        http://localhost/v1/updates/lucia/apply
)"
[[ "$status" == "500" ]]
rmdir "$work_dir/updates/state/operation.json.tmp"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/updates/operation
! grep -Eq '"status":"(queued|running)"' "$work_dir/response.json"

echo "PASS: failed operation persistence leaves the manager retryable"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":"v1.4.0","operationId":"11111111-1111-1111-1111-111111111111"}' \
        http://localhost/v1/updates/lucia/apply
)"
[[ "$status" == "202" ]]
for _ in {1..40}; do
    curl --silent --output "$work_dir/response.json" \
        --unix-socket "$socket_path" \
        http://localhost/v1/updates/operation
    grep -q '"status":"succeeded"' "$work_dir/response.json" && break
    sleep 0.05
done
grep -q '"status":"succeeded"' "$work_dir/response.json"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/updates/operations/11111111-1111-1111-1111-111111111111
grep -q '"operationId":"11111111-1111-1111-1111-111111111111"' \
    "$work_dir/response.json"
grep -qx 'apply lucia v1.4.0' "$update_log"
for _ in {1..40}; do
    grep -q '^--no-block restart lucia-appliance-manager.service lucia-agenthost.service$' \
        "$systemctl_log" && break
    sleep 0.05
done
grep -q '^--no-block restart lucia-appliance-manager.service lucia-agenthost.service$' \
    "$systemctl_log"

echo "PASS: update operations run outside the request lifetime"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":null}' \
        http://localhost/v1/updates/lucia/rollback
)"
[[ "$status" == "202" ]]
for _ in {1..40}; do
    [[ "$(grep -c \
        '^--no-block restart lucia-appliance-manager.service lucia-agenthost.service$' \
        "$systemctl_log")" -ge 2 ]] && break
    sleep 0.05
done
[[ "$(grep -c \
    '^--no-block restart lucia-appliance-manager.service lucia-agenthost.service$' \
    "$systemctl_log")" -ge 2 ]]

echo "PASS: Lucia apply and rollback restart manager and AgentHost together"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":"v1.4.0"}' \
        http://localhost/v1/updates/os/apply
)"
[[ "$status" == "202" ]]
for _ in {1..40}; do
    curl --silent --output "$work_dir/response.json" \
        --unix-socket "$socket_path" \
        http://localhost/v1/updates/operation
    grep -q '"status":"running"' "$work_dir/response.json" && break
    sleep 0.05
done
grep -q '"status":"running"' "$work_dir/response.json"

echo "PASS: OS apply remains nonterminal through boot validation"

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":"v1.5.0"}' \
        http://localhost/v1/updates/lucia/apply
)"
[[ "$status" == "409" ]]
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --request POST \
        http://localhost/v1/services/redis/restart
)"
[[ "$status" == "409" ]]

echo "PASS: pending OS validation blocks overlapping updates"

cat > "$work_dir/updates/state/operation.json.test.tmp" <<'EOF'
{"Action":"apply","Channel":"os","Status":"failed","Tag":"v1.4.0","Message":"OS update failed boot validation; rollback is scheduled."}
EOF
mv "$work_dir/updates/state/operation.json.test.tmp" \
    "$work_dir/updates/state/operation.json"
printf 'status=pending\n' > "$work_dir/updates/state/os.env"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":null}' \
        http://localhost/v1/updates/os/rollback
)"
[[ "$status" == "202" ]]
for _ in {1..40}; do
    curl --silent --output "$work_dir/response.json" \
        --unix-socket "$socket_path" \
        http://localhost/v1/updates/operation
    grep -q '"message":"OS update is awaiting boot validation."' \
        "$work_dir/response.json" && break
    sleep 0.05
done
grep -q '"channel":"os"' "$work_dir/response.json"

echo "PASS: failed OS rollback remains retryable"

cat > "$work_dir/updates/state/operation.json.test.tmp" <<'EOF'
{"Action":"apply","Channel":"os","Status":"failed","Tag":"v1.4.0","Message":"OS update failed boot validation; rollback is scheduled."}
EOF
mv "$work_dir/updates/state/operation.json.test.tmp" \
    "$work_dir/updates/state/operation.json"
printf 'status=rolled-back\n' > "$work_dir/updates/state/os.env"
curl --silent --output "$work_dir/response.json" \
    --unix-socket "$socket_path" \
    http://localhost/v1/updates/operation
grep -q '"status":"failed"' "$work_dir/response.json"
grep -q '"osRollbackAvailable":false' "$work_dir/response.json"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request POST \
        --data '{"tag":null}' \
        http://localhost/v1/updates/os/rollback
)"
[[ "$status" == "409" ]]
grep -q 'OS rollback is not available.' "$work_dir/response.json"

echo "PASS: external OS validation state is reflected without stale rollback"

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
        --data '{"enabled":true,"endpoint":"https://telemetry.example:4317/v1/traces","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
[[ ! -e "$telemetry_environment" ]]

echo "PASS: telemetry rejects OTLP paths unsupported by the Collector"

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
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://telemetry.example:4317","username":"lucia:admin","password":"telemetry-secret","insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
grep -q 'username cannot contain a colon' "$work_dir/response.json"
[[ ! -e "$telemetry_environment" ]]

echo "PASS: telemetry rejects ambiguous Basic usernames"

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
grep -qx -- 'restart lucia-otelcol.service' "$systemctl_log"
! grep -q '^restart lucia-agenthost.service$' "$systemctl_log"

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

status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://other.example:4317","username":null,"password":null,"insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "400" ]]
grep -q 'requires replacing or clearing saved credentials' \
    "$work_dir/response.json"
cmp -s "$work_dir/telemetry.https" "$telemetry_environment"

echo "PASS: saved telemetry credentials remain scoped to their host"

cp "$agenthost_environment" "$work_dir/agenthost.https"
touch "$fail_collector_restart"
status="$(
    curl --silent --output "$work_dir/response.json" --write-out '%{http_code}' \
        --unix-socket "$socket_path" \
        --header 'Content-Type: application/json' \
        --request PUT \
        --data '{"enabled":true,"endpoint":"https://new.example:4317","username":"new","password":"new-secret","insecureSkipVerify":false}' \
        http://localhost/v1/telemetry
)"
[[ "$status" == "503" ]]
cmp -s "$work_dir/telemetry.https" "$telemetry_environment"
cmp -s "$work_dir/agenthost.https" "$agenthost_environment"
[[ "$(grep -c '^restart lucia-otelcol.service$' "$systemctl_log")" -ge 3 ]]

echo "PASS: failed collector restart restores prior telemetry configuration"

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
