#!/usr/bin/env bash

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUNDLE_SCRIPT="$SCRIPT_DIR/build-native-bundle.sh"
WORK="$(mktemp -d "$SCRIPT_DIR/.bundletest.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass_count=0
fail_count=0

pass() {
    printf 'PASS: %s\n' "$1"
    pass_count=$((pass_count + 1))
}

fail() {
    printf 'FAIL: %s\n' "$1"
    fail_count=$((fail_count + 1))
}

test_missing_inputs_show_usage() {
    local output
    local status

    output="$("$BUNDLE_SCRIPT" 2>&1)"
    status=$?

    if [[ "$status" -eq 2 && "$output" == *"Usage:"* ]]; then
        pass "missing required inputs show usage"
    else
        fail "missing required inputs show usage (status=$status output=$output)"
    fi
}

test_help_succeeds() {
    local output
    local status

    output="$("$BUNDLE_SCRIPT" --help 2>&1)"
    status=$?

    if [[ "$status" -eq 0 && "$output" == *"Usage:"* ]]; then
        pass "help shows usage"
    else
        fail "help shows usage (status=$status output=$output)"
    fi
}

test_valid_inputs_create_release_layout() {
    local publish_dir="$WORK/publish"
    local manager_dir="$WORK/manager"
    local dashboard_dir="$WORK/dashboard"
    local redis_server="$WORK/redis-server"
    local output_dir="$WORK/output"
    local output
    local status

    mkdir -p "$publish_dir" "$manager_dir" "$dashboard_dir"
    printf 'agenthost\n' > "$publish_dir/lucia.AgentHost"
    printf 'manager\n' > "$manager_dir/lucia.ApplianceManager"
    printf 'dashboard\n' > "$dashboard_dir/index.html"
    printf 'redis\n' > "$redis_server"
    chmod +x "$publish_dir/lucia.AgentHost" "$redis_server"

    output="$("$BUNDLE_SCRIPT" \
        --version 1.2.3 \
        --publish-dir "$publish_dir" \
        --manager-dir "$manager_dir" \
        --dashboard-dir "$dashboard_dir" \
        --redis-server "$redis_server" \
        --output-dir "$output_dir" 2>&1)"
    status=$?

    if [[ "$status" -ne 0 ]]; then
        fail "valid inputs create release layout (status=$status output=$output)"
        return
    fi

    local release_dir="$output_dir/opt/lucia/releases/1.2.3"
    if cmp -s "$publish_dir/lucia.AgentHost" "$release_dir/app/lucia.AgentHost" \
        && cmp -s "$manager_dir/lucia.ApplianceManager" \
            "$release_dir/manager/lucia.ApplianceManager" \
        && cmp -s "$dashboard_dir/index.html" "$release_dir/app/wwwroot/index.html" \
        && cmp -s "$redis_server" "$release_dir/redis/bin/redis-server"; then
        pass "valid inputs create release layout"
    else
        fail "valid inputs create release layout (expected files missing or changed)"
    fi
}

test_bundle_contains_native_service_contract() {
    local publish_dir="$WORK/service-publish"
    local manager_dir="$WORK/service-manager"
    local dashboard_dir="$WORK/service-dashboard"
    local redis_server="$WORK/service-redis-server"
    local output_dir="$WORK/service-output"
    local output
    local status

    mkdir -p "$publish_dir" "$manager_dir" "$dashboard_dir"
    printf 'agenthost\n' > "$publish_dir/lucia.AgentHost"
    printf 'manager\n' > "$manager_dir/lucia.ApplianceManager"
    printf 'dashboard\n' > "$dashboard_dir/index.html"
    printf 'redis\n' > "$redis_server"

    output="$("$BUNDLE_SCRIPT" \
        --version 1.2.3 \
        --publish-dir "$publish_dir" \
        --manager-dir "$manager_dir" \
        --dashboard-dir "$dashboard_dir" \
        --redis-server "$redis_server" \
        --output-dir "$output_dir" 2>&1)"
    status=$?

    if [[ "$status" -ne 0 ]]; then
        fail "bundle contains native service contract (status=$status output=$output)"
        return
    fi

    local agent_unit="$output_dir/usr/lib/systemd/system/lucia-agenthost.service"
    local manager_unit="$output_dir/usr/lib/systemd/system/lucia-appliance-manager.service"
    local redis_unit="$output_dir/usr/lib/systemd/system/lucia-redis.service"
    local redis_config="$output_dir/etc/lucia/redis.conf"
    local recovery_shell="$output_dir/usr/libexec/lucia/lucia-recovery-shell"
    local environment="$output_dir/var/lib/lucia/config/lucia.env"
    local sysusers="$output_dir/usr/lib/sysusers.d/lucia.conf"
    local tmpfiles="$output_dir/usr/lib/tmpfiles.d/lucia.conf"

    if [[ -f "$agent_unit" && -f "$manager_unit" && -f "$redis_unit" \
            && -f "$redis_config" && -f "$environment" ]] \
        && grep -q '^exec /usr/bin/nmtui' "$recovery_shell" \
        && ! grep -q '^m lucia-telemetry lucia$' "$sysusers" \
        && grep -q '^ExecStart=/opt/lucia/current/manager/lucia.ApplianceManager$' "$manager_unit" \
        && grep -q '^Environment=LUCIA_APPLIANCE_SOCKET=/run/lucia-appliance/appliance-manager.sock$' "$manager_unit" \
        && grep -q '^User=root$' "$manager_unit" \
        && grep -q '^Group=lucia$' "$manager_unit" \
        && grep -q '^ReadWritePaths=/etc/lucia /etc/systemd/system/multi-user.target.wants /opt/lucia /var/lib/lucia$' "$manager_unit" \
        && grep -q '^CapabilityBoundingSet=CAP_SYS_BOOT$' "$manager_unit" \
        && grep -q '^Wants=.*lucia-redis.service' "$agent_unit" \
        && ! grep -q '^Requires=lucia-redis.service$' "$agent_unit" \
        && grep -q '^ExecStartPre=+/usr/libexec/lucia/lucia-renew-tls$' "$agent_unit" \
        && grep -q '^ExecStart=/opt/lucia/current/app/lucia.AgentHost$' "$agent_unit" \
        && grep -q '^Restart=always$' "$agent_unit" \
        && ! grep -q '^StateDirectory=' "$agent_unit" \
        && grep -q '^ReadWritePaths=/var/lib/lucia/config/tls /var/lib/lucia/db /var/lib/lucia/models /var/lib/lucia/plugins /var/lib/lucia/voice-clips$' "$agent_unit" \
        && ! grep -q '^PrivateDevices=true$' "$agent_unit" \
        && grep -q '^d /var/lib/lucia 0755 root root -$' "$tmpfiles" \
        && grep -q '^ExecStart=/opt/lucia/current/redis/bin/redis-server /etc/lucia/redis.conf$' "$redis_unit" \
        && grep -q '^appendonly yes$' "$redis_config" \
        && grep -q '^maxmemory-policy noeviction$' "$redis_config" \
        && grep -q '^maxmemory 512mb$' "$redis_config" \
        && grep -q '^MemoryMax=768M$' "$redis_unit" \
        && grep -q '^dir /var/lib/lucia/redis$' "$redis_config" \
        && grep -q '^DataProvider__Cache=Redis$' "$environment" \
        && grep -q '^DataProvider__Store=SQLite$' "$environment" \
        && grep -q '^Appliance__Mode=Installed$' "$environment" \
        && grep -q '^ASPNETCORE_URLS=http://127.0.0.1:8098;https://0.0.0.0:8099$' "$environment" \
        && grep -q '^Kestrel__Certificates__Default__Path=/var/lib/lucia/config/tls/agenthost.crt$' "$environment" \
        && grep -q '^Kestrel__Certificates__Default__KeyPath=/var/lib/lucia/config/tls/agenthost.key$' "$environment" \
        && ! grep -q '^Appliance__Enabled=' "$environment" \
        && grep -q '^Observability__Mode=Off$' "$environment"; then
        pass "bundle contains native service contract"
    else
        fail "bundle contains native service contract (required settings missing)"
    fi
}

test_voice_assets_are_baked_into_bundle() {
    local publish_dir="$WORK/voice-publish"
    local manager_dir="$WORK/voice-manager"
    local dashboard_dir="$WORK/voice-dashboard"
    local redis_server="$WORK/voice-redis-server"
    local native_dir="$WORK/voice-native"
    local models_dir="$WORK/voice-models"
    local plugins_dir="$WORK/voice-plugins"
    local output_dir="$WORK/voice-output"
    local output
    local status

    mkdir -p \
        "$publish_dir/runtimes/linux-arm64/native" \
        "$manager_dir" \
        "$dashboard_dir" \
        "$native_dir" \
        "$models_dir/stt/default" \
        "$plugins_dir/example"
    printf 'agenthost\n' > "$publish_dir/lucia.AgentHost"
    printf 'manager\n' > "$manager_dir/lucia.ApplianceManager"
    printf 'cpu-native\n' > "$publish_dir/runtimes/linux-arm64/native/libonnxruntime.so"
    printf 'dashboard\n' > "$dashboard_dir/index.html"
    printf 'redis\n' > "$redis_server"
    printf 'gpu-native\n' > "$native_dir/libonnxruntime.so"
    printf 'cuda-provider\n' > "$native_dir/libonnxruntime_providers_cuda.so"
    printf 'shared-provider\n' > "$native_dir/libonnxruntime_providers_shared.so"
    printf 'sherpa\n' > "$native_dir/libsherpa-onnx-c-api.so"
    printf 'model\n' > "$models_dir/stt/default/model.onnx"
    printf 'plugin\n' > "$plugins_dir/example/plugin.cs"

    output="$("$BUNDLE_SCRIPT" \
        --version 1.2.3 \
        --publish-dir "$publish_dir" \
        --manager-dir "$manager_dir" \
        --dashboard-dir "$dashboard_dir" \
        --redis-server "$redis_server" \
        --native-dir "$native_dir" \
        --models-dir "$models_dir" \
        --plugins-dir "$plugins_dir" \
        --output-dir "$output_dir" 2>&1)"
    status=$?

    if [[ "$status" -ne 0 ]]; then
        fail "voice assets are baked into bundle (status=$status output=$output)"
        return
    fi

    local release_dir="$output_dir/opt/lucia/releases/1.2.3"
    if cmp -s "$native_dir/libonnxruntime.so" \
            "$release_dir/app/runtimes/linux-arm64/native/libonnxruntime.so" \
        && cmp -s "$models_dir/stt/default/model.onnx" \
            "$output_dir/var/lib/lucia/models/stt/default/model.onnx" \
        && cmp -s "$plugins_dir/example/plugin.cs" \
            "$output_dir/var/lib/lucia/plugins/example/plugin.cs"; then
        pass "voice assets are baked into bundle"
    else
        fail "voice assets are baked into bundle (expected files missing or changed)"
    fi
}

test_telemetry_assets_are_installed_but_disabled() {
    local publish_dir="$WORK/telemetry-publish"
    local manager_dir="$WORK/telemetry-manager"
    local dashboard_dir="$WORK/telemetry-dashboard"
    local redis_server="$WORK/telemetry-redis-server"
    local otelcol="$WORK/otelcol-contrib"
    local redis_exporter="$WORK/redis_exporter"
    local output_dir="$WORK/telemetry-output"
    local output
    local status

    mkdir -p "$publish_dir" "$manager_dir" "$dashboard_dir"
    printf 'agenthost\n' > "$publish_dir/lucia.AgentHost"
    printf 'manager\n' > "$manager_dir/lucia.ApplianceManager"
    printf 'dashboard\n' > "$dashboard_dir/index.html"
    printf 'redis\n' > "$redis_server"
    printf 'collector\n' > "$otelcol"
    printf 'exporter\n' > "$redis_exporter"

    output="$("$BUNDLE_SCRIPT" \
        --version 1.2.3 \
        --publish-dir "$publish_dir" \
        --manager-dir "$manager_dir" \
        --dashboard-dir "$dashboard_dir" \
        --redis-server "$redis_server" \
        --otelcol "$otelcol" \
        --redis-exporter "$redis_exporter" \
        --output-dir "$output_dir" 2>&1)"
    status=$?

    if [[ "$status" -ne 0 ]]; then
        fail "telemetry assets are installed but disabled (status=$status output=$output)"
        return
    fi

    local release_dir="$output_dir/opt/lucia/releases/1.2.3"
    if cmp -s "$otelcol" "$release_dir/telemetry/bin/otelcol-contrib" \
        && cmp -s "$redis_exporter" "$release_dir/telemetry/bin/redis_exporter" \
        && [[ -f "$output_dir/usr/lib/systemd/system/lucia-otelcol.service" ]] \
        && [[ -f "$output_dir/usr/lib/systemd/system/lucia-redis-exporter.service" ]] \
        && [[ ! -e "$output_dir/etc/systemd/system/multi-user.target.wants/lucia-otelcol.service" ]] \
        && [[ ! -e "$output_dir/etc/systemd/system/multi-user.target.wants/lucia-redis-exporter.service" ]]; then
        pass "telemetry assets are installed but disabled"
    else
        fail "telemetry assets are installed but disabled (contract mismatch)"
    fi
}

test_tls_material_is_reused_and_rotated() {
    if [[ "$(uname -s)" != "Linux" ]]; then
        pass "TLS renewal runtime check requires Linux"
        return
    fi

    local tls_dir="$WORK/tls"
    local hostname_file="$WORK/hostname"
    local renew="$SCRIPT_DIR/rootfs/usr/libexec/lucia/lucia-renew-tls"
    local group
    local original
    local reused
    local rotated
    group="$(id -gn)"
    printf 'lucia-test\n' > "$hostname_file"

    sudo env \
        LUCIA_HOSTNAME_PATH="$hostname_file" \
        LUCIA_TLS_DIR="$tls_dir" \
        LUCIA_TLS_GROUP="$group" \
        "$renew"
    original="$(sha256sum "$tls_dir/agenthost.crt")"
    sudo env \
        LUCIA_HOSTNAME_PATH="$hostname_file" \
        LUCIA_TLS_DIR="$tls_dir" \
        LUCIA_TLS_GROUP="$group" \
        "$renew"
    reused="$(sha256sum "$tls_dir/agenthost.crt")"
    sudo env \
        LUCIA_HOSTNAME_PATH="$hostname_file" \
        LUCIA_TLS_DIR="$tls_dir" \
        LUCIA_TLS_GROUP="$group" \
        LUCIA_TLS_RENEW_BEFORE_SECONDS=999999999 \
        "$renew"
    rotated="$(sha256sum "$tls_dir/agenthost.crt")"

    if [[ "$original" == "$reused" && "$rotated" != "$original" ]] \
        && openssl x509 -in "$tls_dir/agenthost.crt" \
            -checkhost lucia-test.local -noout >/dev/null; then
        pass "TLS material is reused and rotated before expiry"
    else
        fail "TLS material is reused and rotated before expiry"
    fi
    sudo rm -rf "$tls_dir"
}

test_missing_inputs_show_usage
test_help_succeeds
test_valid_inputs_create_release_layout
test_bundle_contains_native_service_contract
test_voice_assets_are_baked_into_bundle
test_telemetry_assets_are_installed_but_disabled
test_tls_material_is_reused_and_rotated

printf '\n%d passed, %d failed\n' "$pass_count" "$fail_count"
[[ "$fail_count" -eq 0 ]]
