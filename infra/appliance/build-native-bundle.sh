#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: build-native-bundle.sh \
  --version VERSION \
  --publish-dir DIRECTORY \
  --manager-dir DIRECTORY \
  --dashboard-dir DIRECTORY \
  --redis-server FILE \
  [--native-dir DIRECTORY --models-dir DIRECTORY --plugins-dir DIRECTORY] \
  [--otelcol FILE --redis-exporter FILE] \
  --output-dir DIRECTORY
EOF
}

die_usage() {
    printf 'Error: %s\n\n' "$1" >&2
    usage >&2
    exit 2
}

die() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

version=""
publish_dir=""
manager_dir=""
dashboard_dir=""
redis_server=""
native_dir=""
models_dir=""
plugins_dir=""
otelcol=""
redis_exporter=""
output_dir=""
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version|--publish-dir|--manager-dir|--dashboard-dir|--redis-server|--native-dir|--models-dir|--plugins-dir|--otelcol|--redis-exporter|--output-dir)
            [[ $# -ge 2 ]] || die_usage "$1 requires a value"
            case "$1" in
                --version) version="$2" ;;
                --publish-dir) publish_dir="$2" ;;
                --manager-dir) manager_dir="$2" ;;
                --dashboard-dir) dashboard_dir="$2" ;;
                --redis-server) redis_server="$2" ;;
                --native-dir) native_dir="$2" ;;
                --models-dir) models_dir="$2" ;;
                --plugins-dir) plugins_dir="$2" ;;
                --otelcol) otelcol="$2" ;;
                --redis-exporter) redis_exporter="$2" ;;
                --output-dir) output_dir="$2" ;;
            esac
            shift 2
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            die_usage "unknown option: $1"
            ;;
    esac
done

[[ -n "$version" ]] || die_usage "--version is required"
[[ -n "$publish_dir" ]] || die_usage "--publish-dir is required"
[[ -n "$manager_dir" ]] || die_usage "--manager-dir is required"
[[ -n "$dashboard_dir" ]] || die_usage "--dashboard-dir is required"
[[ -n "$redis_server" ]] || die_usage "--redis-server is required"
[[ -n "$output_dir" ]] || die_usage "--output-dir is required"

[[ "$version" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]] \
    || die_usage "--version must be a filesystem-safe release identifier"
[[ -f "$publish_dir/lucia.AgentHost" ]] \
    || die "publish directory must contain lucia.AgentHost"
[[ -f "$manager_dir/lucia.ApplianceManager" ]] \
    || die "manager directory must contain lucia.ApplianceManager"
[[ -f "$dashboard_dir/index.html" ]] \
    || die "dashboard directory must contain index.html"
[[ -f "$redis_server" ]] \
    || die "Redis input must be a file"
[[ ! -e "$output_dir" ]] \
    || die "output directory already exists: $output_dir"

voice_input_count=0
[[ -n "$native_dir" ]] && voice_input_count=$((voice_input_count + 1))
[[ -n "$models_dir" ]] && voice_input_count=$((voice_input_count + 1))
[[ -n "$plugins_dir" ]] && voice_input_count=$((voice_input_count + 1))
[[ "$voice_input_count" -eq 0 || "$voice_input_count" -eq 3 ]] \
    || die_usage "--native-dir, --models-dir, and --plugins-dir must be provided together"

if [[ "$voice_input_count" -eq 3 ]]; then
    [[ -d "$native_dir" ]] || die "native directory does not exist: $native_dir"
    [[ -d "$models_dir" ]] || die "models directory does not exist: $models_dir"
    [[ -d "$plugins_dir" ]] || die "plugins directory does not exist: $plugins_dir"

    for library in \
        libonnxruntime.so \
        libonnxruntime_providers_cuda.so \
        libonnxruntime_providers_shared.so \
        libsherpa-onnx-c-api.so; do
        [[ -e "$native_dir/$library" ]] \
            || die "native directory is missing required library: $library"
    done

    [[ -n "$(find "$models_dir" -type f -name '*.onnx' -print -quit)" ]] \
        || die "models directory must contain at least one ONNX model"
fi

telemetry_input_count=0
[[ -n "$otelcol" ]] && telemetry_input_count=$((telemetry_input_count + 1))
[[ -n "$redis_exporter" ]] && telemetry_input_count=$((telemetry_input_count + 1))
[[ "$telemetry_input_count" -eq 0 || "$telemetry_input_count" -eq 2 ]] \
    || die_usage "--otelcol and --redis-exporter must be provided together"
if [[ "$telemetry_input_count" -eq 2 ]]; then
    [[ -f "$otelcol" ]] || die "Collector input must be a file"
    [[ -f "$redis_exporter" ]] || die "Redis exporter input must be a file"
fi

release_dir="$output_dir/opt/lucia/releases/$version"
mkdir -p "$release_dir/app/wwwroot" "$release_dir/manager" "$release_dir/redis/bin"
cp -a "$script_dir/rootfs/." "$output_dir/"
cp -a "$publish_dir/." "$release_dir/app/"
cp -a "$manager_dir/." "$release_dir/manager/"
cp -a "$dashboard_dir/." "$release_dir/app/wwwroot/"
cp "$redis_server" "$release_dir/redis/bin/redis-server"

if [[ "$voice_input_count" -eq 3 ]]; then
    find "$release_dir/app" -type f \
        \( -name 'libonnxruntime.so*' \
        -o -name 'libonnxruntime_providers_*.so' \
        -o -name 'libsherpa-onnx-c-api.so*' \) \
        -delete
    mkdir -p \
        "$release_dir/app/runtimes/linux-arm64/native" \
        "$output_dir/var/lib/lucia/models" \
        "$output_dir/var/lib/lucia/plugins"
    cp -a "$native_dir/." "$release_dir/app/runtimes/linux-arm64/native/"
    cp -a "$models_dir/." "$output_dir/var/lib/lucia/models/"
    cp -a "$plugins_dir/." "$output_dir/var/lib/lucia/plugins/"
    chmod 0755 "$release_dir/app/runtimes/linux-arm64/native/"*.so*
fi

if [[ "$telemetry_input_count" -eq 2 ]]; then
    mkdir -p "$release_dir/telemetry/bin"
    cp "$otelcol" "$release_dir/telemetry/bin/otelcol-contrib"
    cp "$redis_exporter" "$release_dir/telemetry/bin/redis_exporter"
    chmod 0755 \
        "$release_dir/telemetry/bin/otelcol-contrib" \
        "$release_dir/telemetry/bin/redis_exporter"
fi

chmod 0755 \
    "$release_dir/app/lucia.AgentHost" \
    "$release_dir/manager/lucia.ApplianceManager" \
    "$release_dir/redis/bin/redis-server"
chmod 0640 "$output_dir/var/lib/lucia/config/lucia.env"
chmod 0644 \
    "$output_dir/etc/lucia/otelcol.yaml" \
    "$output_dir/etc/lucia/redis.conf" \
    "$output_dir/etc/lucia/telemetry.env.example" \
    "$output_dir/usr/lib/systemd/system/lucia-agenthost.service" \
    "$output_dir/usr/lib/systemd/system/lucia-appliance-manager.service" \
    "$output_dir/usr/lib/systemd/system/lucia-otelcol.service" \
    "$output_dir/usr/lib/systemd/system/lucia-redis.service" \
    "$output_dir/usr/lib/systemd/system/lucia-redis-exporter.service" \
    "$output_dir/usr/lib/sysusers.d/lucia.conf" \
    "$output_dir/usr/lib/tmpfiles.d/lucia.conf"
ln -s "releases/$version" "$output_dir/opt/lucia/current"

printf 'Native bundle created at %s\n' "$output_dir"
