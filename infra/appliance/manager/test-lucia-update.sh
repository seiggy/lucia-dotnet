#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
updater="$repo_root/infra/appliance/rootfs/usr/libexec/lucia/lucia-update"
os_validator="$repo_root/infra/appliance/rootfs/usr/libexec/lucia/lucia-validate-os-update"
work="$(mktemp -d)"
loop_device=""

cleanup() {
    [[ -z "$loop_device" ]] || losetup --detach "$loop_device" 2>/dev/null || true
    rm -rf "$work"
}
trap cleanup EXIT

mkdir -p \
    "$work/bin" \
    "$work/releases/1.0.0/app" \
    "$work/data/config" \
    "$work/data/db" \
    "$work/data/plugins" \
    "$work/data/redis" \
    "$work/updates/staging" \
    "$work/updates/backups"
ln -s releases/1.0.0 "$work/current"
printf 'old-app\n' > "$work/releases/1.0.0/app/version"
printf 'old-db\n' > "$work/data/db/lucia.db"
printf 'old-plugin\n' > "$work/data/plugins/official.plugin"
printf 'old-redis-config\n' > "$work/redis.conf"
printf 'trusted\n' > "$work/trusted-root.jsonl"
printf '1.0.0\n' > "$work/os-version"
cat > "$work/runtime.json" <<'EOF'
{"layoutVersion":1,"dataSchemaVersion":1,"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"}
EOF

cat > "$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
[[ "$*" == *"attestation verify"* ]]
[[ "$*" == *"--repo seiggy/lucia-dotnet"* ]]
[[ "$*" == *"--signer-workflow seiggy/lucia-dotnet/.github/workflows/appliance-release.yml"* ]]
[[ "$*" == *"--bundle "*"lucia-appliance-attestations.jsonl"* ]]
[[ "$*" == *"--custom-trusted-root"* ]]
[[ ! -e "$LUCIA_TEST_REJECT_ATTESTATION" ]]
EOF
chmod +x "$work/bin/gh"

cat > "$work/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$LUCIA_TEST_SYSTEMCTL_LOG"
EOF
chmod +x "$work/bin/systemctl"
cat > "$work/bin/curl" <<'EOF'
#!/usr/bin/env bash
[[ ! -e "$LUCIA_TEST_FAIL_HEALTH_FILE" ]]
EOF
chmod +x "$work/bin/curl"

cat > "$work/bin/nvbootctrl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" == "-t rootfs get-current-slot" ]]; then
    cat "$LUCIA_TEST_CURRENT_SLOT"
elif [[ "$*" == "-t rootfs mark-boot-successful" ]]; then
    touch "$LUCIA_TEST_BOOT_SUCCESSFUL"
elif [[ "$*" == "-t rootfs set-active-boot-slot "* ]]; then
    printf '%s\n' "${*: -1}" > "$LUCIA_TEST_ACTIVE_SLOT"
else
    exit 2
fi
EOF
chmod +x "$work/bin/nvbootctrl"

write_manifest() {
    local channel="$1"
    local tag="$2"
    local version="$3"
    local payload="$4"
    local stage="$work/updates/staging/$tag"
    local payload_name
    local payload_hash
    local payload_bytes
    local requirements

    payload_name="$(basename "$payload")"
    payload_hash="$(sha256sum "$payload" | cut -d' ' -f1)"
    payload_bytes="$(stat --format '%s' "$payload")"
    mkdir -p "$stage"
    cp "$payload" "$stage/$payload_name"
    printf 'bundle\n' > "$stage/lucia-appliance-attestations.jsonl"
    if [[ "$channel" == "lucia" ]]; then
        requirements='"jetsonLinux":"36.5.2","layoutVersion":1,"dataSchemaVersion":1,"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34","reboot":false'
    else
        requirements='"minimumLuciaVersion":"1.0.0","layoutVersion":1,"reboot":true'
    fi
    cat > "$stage/lucia-appliance-manifest.json" <<EOF
{"schemaVersion":1,"repository":"seiggy/lucia-dotnet","tag":"$tag","version":"$version","releaseApi":"https://api.github.com/repos/seiggy/lucia-dotnet/releases/tags/$tag","releaseNotesUrl":"https://github.com/seiggy/lucia-dotnet/releases/tag/$tag","compatibility":{"architecture":"arm64","board":"jetson-orin-nano-super-p3767-0005","minimumDiskBytes":61203283968,"layoutVersion":1,"dataSchemaVersion":1,"redis":"8.2.9","cuda":"12.6","cudnn":"9.3.0.75","onnxRuntime":"1.23.2","sherpaOnnx":"1.12.34"},"channels":{"$channel":{"version":"$version","bytes":$payload_bytes,"sha256":"$payload_hash","requires":{$requirements},"parts":[{"name":"$payload_name","bytes":$payload_bytes,"sha256":"$payload_hash","url":"https://github.com/seiggy/lucia-dotnet/releases/download/$tag/$payload_name"}]}}}
EOF
}

run_update() {
    LUCIA_UPDATE_ROOT="$work/updates" \
    LUCIA_DATA_ROOT="$work/data" \
    LUCIA_CURRENT_LINK="$work/current" \
    LUCIA_RELEASES_DIR="$work/releases" \
    LUCIA_GH_PATH="$work/bin/gh" \
    LUCIA_TRUSTED_ROOT_PATH="$work/trusted-root.jsonl" \
    LUCIA_SYSTEMCTL_PATH="$work/bin/systemctl" \
    LUCIA_CURL_PATH="$work/bin/curl" \
    LUCIA_NVBOOTCTRL_PATH="$work/bin/nvbootctrl" \
    LUCIA_PARTLABEL_DIR="$work/by-partlabel" \
    LUCIA_OS_VERSION_PATH="$work/os-version" \
    LUCIA_REDIS_CONFIG_PATH="$work/redis.conf" \
    LUCIA_RUNTIME_INFO_PATH="$work/runtime.json" \
    LUCIA_ARCHITECTURE=arm64 \
    LUCIA_APPLIANCE_BOARD=jetson-orin-nano-super-p3767-0005 \
    LUCIA_STORAGE_BYTES=61203283968 \
    LUCIA_JETSON_LINUX_VERSION=36.5.2 \
    LUCIA_TEST_SYSTEMCTL_LOG="$work/systemctl.log" \
    LUCIA_TEST_REJECT_ATTESTATION="$work/reject-attestation" \
    LUCIA_TEST_CURRENT_SLOT="$work/current-slot" \
    LUCIA_TEST_ACTIVE_SLOT="$work/active-slot" \
    LUCIA_TEST_BOOT_SUCCESSFUL="$work/boot-successful" \
    LUCIA_TEST_FAIL_HEALTH_FILE="$work/fail-health" \
    LUCIA_UPDATE_HEALTH_ATTEMPTS=1 \
    LUCIA_UPDATE_HEALTH_DELAY_SECONDS=0 \
        "$updater" "$@"
}

grep -q '^set -Eeuo pipefail$' "$updater"

mkdir -p "$work/lucia-payload/opt/lucia/releases/1.1.0/app"
mkdir -p "$work/lucia-payload/etc/lucia" "$work/lucia-payload/var/lib/lucia/plugins"
printf 'new-app\n' > "$work/lucia-payload/opt/lucia/releases/1.1.0/app/version"
printf 'new-plugin\n' > "$work/lucia-payload/var/lib/lucia/plugins/official.plugin"
printf 'new-redis-config\n' > "$work/lucia-payload/etc/lucia/redis.conf"
tar -I zstd -cf "$work/lucia.tar.zst" -C "$work/lucia-payload" .
write_manifest lucia v1.1.0 1.1.0 "$work/lucia.tar.zst"

touch "$work/reject-attestation"
if run_update apply lucia v1.1.0; then
    echo "Unverified Lucia update was accepted" >&2
    exit 1
fi
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
rm "$work/reject-attestation"
printf 'tampered\n' >> "$work/updates/staging/v1.1.0/lucia.tar.zst"
if run_update apply lucia v1.1.0; then
    echo "Tampered Lucia update was accepted" >&2
    exit 1
fi
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
write_manifest lucia v1.1.0 1.1.0 "$work/lucia.tar.zst"
sed -i 's/jetson-orin-nano-super-p3767-0005/unsupported-board/' \
    "$work/updates/staging/v1.1.0/lucia-appliance-manifest.json"
if run_update apply lucia v1.1.0; then
    echo "Hardware-incompatible Lucia update was accepted" >&2
    exit 1
fi
write_manifest lucia v1.1.0 1.1.0 "$work/lucia.tar.zst"
sed -i 's/"cuda":"12.6"/"cuda":"99.0"/' \
    "$work/updates/staging/v1.1.0/lucia-appliance-manifest.json"
if run_update apply lucia v1.1.0; then
    echo "Runtime-incompatible Lucia update was accepted" >&2
    exit 1
fi
write_manifest lucia v1.1.0 1.1.0 "$work/lucia.tar.zst"

run_update apply lucia v1.1.0
[[ "$(readlink "$work/current")" == "releases/1.1.0" ]]
grep -qx 'new-app' "$work/releases/1.1.0/app/version"
grep -qx 'new-plugin' "$work/data/plugins/official.plugin"
grep -qx 'new-redis-config' "$work/redis.conf"
grep -qx 'old-db' "$work/data/db/lucia.db"
grep -qx 'stop lucia-agenthost.service lucia-redis.service' "$work/systemctl.log"
grep -qx 'start lucia-redis.service lucia-agenthost.service' "$work/systemctl.log"

printf 'migrated-db\n' > "$work/data/db/lucia.db"
run_update rollback lucia
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
grep -qx 'old-db' "$work/data/db/lucia.db"
grep -qx 'old-plugin' "$work/data/plugins/official.plugin"
grep -qx 'old-redis-config' "$work/redis.conf"

echo "PASS: Lucia update verifies, switches atomically, and rolls back data"

write_manifest lucia v1.1.0 1.1.0 "$work/lucia.tar.zst"
printf 'current-db\n' > "$work/data/db/lucia.db"
rm -rf "$work/data/plugins"
if run_update apply lucia v1.1.0; then
    echo "Lucia update with a failed backup was accepted" >&2
    exit 1
fi
grep -qx 'current-db' "$work/data/db/lucia.db"
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
mkdir -p "$work/data/plugins"
printf 'old-plugin\n' > "$work/data/plugins/official.plugin"
printf 'old-db\n' > "$work/data/db/lucia.db"

echo "PASS: failed backup never restores stale data from an earlier attempt"

mkdir -p "$work/lucia-payload-recover/opt/lucia/releases/1.1.1/app"
mkdir -p "$work/lucia-payload-recover/etc/lucia" "$work/lucia-payload-recover/var/lib/lucia/plugins"
printf 'recover-app\n' > "$work/lucia-payload-recover/opt/lucia/releases/1.1.1/app/version"
cp "$work/lucia-payload/etc/lucia/redis.conf" "$work/lucia-payload-recover/etc/lucia/redis.conf"
cp "$work/lucia-payload/var/lib/lucia/plugins/official.plugin" "$work/lucia-payload-recover/var/lib/lucia/plugins/official.plugin"
tar -I zstd -cf "$work/lucia-recover.tar.zst" -C "$work/lucia-payload-recover" .
write_manifest lucia v1.1.1 1.1.1 "$work/lucia-recover.tar.zst"
run_update apply lucia v1.1.1
printf 'interrupted-db\n' > "$work/data/db/lucia.db"
sed -i 's/^phase=committed$/phase=switched/' "$work/updates/state/lucia.env"
run_update recover lucia
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
grep -qx 'old-db' "$work/data/db/lucia.db"

echo "PASS: startup recovery reverses an interrupted Lucia transaction"

mkdir -p "$work/lucia-payload-2/opt/lucia/releases/1.2.0/app"
mkdir -p "$work/lucia-payload-2/etc/lucia" "$work/lucia-payload-2/var/lib/lucia/plugins"
printf 'bad-app\n' > "$work/lucia-payload-2/opt/lucia/releases/1.2.0/app/version"
cp "$work/lucia-payload/etc/lucia/redis.conf" "$work/lucia-payload-2/etc/lucia/redis.conf"
cp "$work/lucia-payload/var/lib/lucia/plugins/official.plugin" "$work/lucia-payload-2/var/lib/lucia/plugins/official.plugin"
tar -I zstd -cf "$work/lucia-2.tar.zst" -C "$work/lucia-payload-2" .
write_manifest lucia v1.2.0 1.2.0 "$work/lucia-2.tar.zst"
touch "$work/fail-health"
if run_update apply lucia v1.2.0; then
    echo "Unhealthy Lucia update was accepted" >&2
    exit 1
fi
rm "$work/fail-health"
[[ "$(readlink "$work/current")" == "releases/1.0.0" ]]
grep -qx 'old-db' "$work/data/db/lucia.db"

echo "PASS: unhealthy Lucia release restores its predecessor automatically"
[[ "$(grep -c '^stop lucia-agenthost.service lucia-redis.service$' \
    "$work/systemctl.log")" -ge 4 ]]

truncate --size 384M "$work/disk.img"
loop_device="$(losetup --find --show --partscan "$work/disk.img")"
sgdisk \
    --new=1:2048:+96M --change-name=1:APP \
    --new=2:0:+96M --change-name=2:APP_b \
    --new=3:0:+16M --change-name=3:A_kernel \
    --new=4:0:+16M --change-name=4:B_kernel \
    --new=5:0:+4M --change-name=5:A_kernel-dtb \
    --new=6:0:+4M --change-name=6:B_kernel-dtb \
    "$loop_device" >/dev/null
partx --update "$loop_device"
udevadm settle
mkdir -p "$work/by-partlabel" "$work/os-payload"
for entry in \
    "APP ${loop_device}p1" \
    "APP_b ${loop_device}p2" \
    "A_kernel ${loop_device}p3" \
    "B_kernel ${loop_device}p4" \
    "A_kernel-dtb ${loop_device}p5" \
    "B_kernel-dtb ${loop_device}p6"; do
    read -r label device <<< "$entry"
    ln -s "$device" "$work/by-partlabel/$label"
done
truncate --size 64M "$work/os-payload/system.img_b"
mkfs.ext4 -q -F "$work/os-payload/system.img_b"
printf 'boot-b\n' > "$work/os-payload/boot.img_b"
printf 'dtb-b\n' > "$work/os-payload/kernel_test.dtb"
tar -I zstd -cf "$work/os.tar.zst" -C "$work/os-payload" \
    system.img_b boot.img_b kernel_test.dtb
write_manifest os v1.1.0 1.1.0 "$work/os.tar.zst"
printf '0\n' > "$work/current-slot"

run_update apply os v1.1.0
[[ "$(cat "$work/active-slot")" == "1" ]]
e2fsck -fn "${loop_device}p2" >/dev/null
[[ "$(dd if="${loop_device}p4" bs=7 count=1 status=none)" == "boot-b" ]]
[[ "$(dd if="${loop_device}p6" bs=6 count=1 status=none)" == "dtb-b" ]]
grep -qx -- '--no-block reboot' "$work/systemctl.log"
printf '1\n' > "$work/current-slot"
LUCIA_UPDATE_ROOT="$work/updates" \
LUCIA_NVBOOTCTRL_PATH="$work/bin/nvbootctrl" \
LUCIA_SYSTEMCTL_PATH="$work/bin/systemctl" \
LUCIA_CURL_PATH="$work/bin/curl" \
LUCIA_TEST_CURRENT_SLOT="$work/current-slot" \
LUCIA_TEST_ACTIVE_SLOT="$work/active-slot" \
LUCIA_TEST_BOOT_SUCCESSFUL="$work/boot-successful" \
LUCIA_TEST_SYSTEMCTL_LOG="$work/systemctl.log" \
LUCIA_UPDATE_HEALTH_ATTEMPTS=1 \
LUCIA_UPDATE_HEALTH_DELAY_SECONDS=0 \
    "$os_validator"
grep -qx 'status=validated' "$work/updates/state/os.env"
grep -q '"Status":"succeeded"' "$work/updates/state/operation.json"
[[ -e "$work/boot-successful" ]]

run_update rollback os
[[ "$(cat "$work/active-slot")" == "0" ]]
grep -qx 'status=rollback-pending' "$work/updates/state/os.env"

echo "PASS: OS update writes only the inactive slot and reverses boot selection"

write_manifest os v1.2.0 1.2.0 "$work/os.tar.zst"
printf '0\n' > "$work/current-slot"
run_update apply os v1.2.0
printf '1\n' > "$work/current-slot"
touch "$work/fail-health"
LUCIA_UPDATE_ROOT="$work/updates" \
    LUCIA_NVBOOTCTRL_PATH="$work/bin/nvbootctrl" \
    LUCIA_SYSTEMCTL_PATH="$work/bin/systemctl" \
    LUCIA_CURL_PATH="$work/bin/curl" \
    LUCIA_TEST_CURRENT_SLOT="$work/current-slot" \
    LUCIA_TEST_ACTIVE_SLOT="$work/active-slot" \
    LUCIA_TEST_BOOT_SUCCESSFUL="$work/boot-successful" \
    LUCIA_TEST_SYSTEMCTL_LOG="$work/systemctl.log" \
    LUCIA_TEST_FAIL_HEALTH_FILE="$work/fail-health" \
    LUCIA_UPDATE_HEALTH_ATTEMPTS=1 \
    LUCIA_UPDATE_HEALTH_DELAY_SECONDS=0 \
        "$os_validator"
rm "$work/fail-health"
[[ "$(cat "$work/active-slot")" == "0" ]]
grep -qx 'status=rollback-pending' "$work/updates/state/os.env"
grep -q '"Status":"failed"' "$work/updates/state/operation.json"
printf '0\n' > "$work/current-slot"
LUCIA_UPDATE_ROOT="$work/updates" \
    LUCIA_NVBOOTCTRL_PATH="$work/bin/nvbootctrl" \
    LUCIA_SYSTEMCTL_PATH="$work/bin/systemctl" \
    LUCIA_CURL_PATH="$work/bin/curl" \
    LUCIA_TEST_CURRENT_SLOT="$work/current-slot" \
    LUCIA_TEST_ACTIVE_SLOT="$work/active-slot" \
    LUCIA_TEST_BOOT_SUCCESSFUL="$work/boot-successful" \
    LUCIA_TEST_SYSTEMCTL_LOG="$work/systemctl.log" \
        "$os_validator"
grep -qx 'status=rolled-back' "$work/updates/state/os.env"
grep -q '"Action":"rollback"' "$work/updates/state/operation.json"

echo "PASS: unhealthy OS slot selects its predecessor before reboot"
