#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: build-release-assets.sh --version VERSION --output-dir DIR --work-dir DIR
EOF
}

die() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

version=""
output_dir=""
work_dir=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            version="${2:-}"
            shift 2
            ;;
        --output-dir)
            output_dir="${2:-}"
            shift 2
            ;;
        --work-dir)
            work_dir="${2:-}"
            shift 2
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            exit 2
            ;;
    esac
done

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
    || die "--version must match MAJOR.MINOR.PATCH"
[[ -n "$output_dir" && -n "$work_dir" ]] \
    || die "--output-dir and --work-dir are required"
[[ "$(uname -s)" == "Linux" && "$(uname -m)" == "x86_64" ]] \
    || die "a native x86_64 Linux build host is required"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
source "$script_dir/appliance.lock"

for command in curl docker dotnet findmnt mountpoint npm openssl python3 sha1sum sha256sum tar umount zstd; do
    command -v "$command" >/dev/null || die "required command is missing: $command"
done
sudo -n true 2>/dev/null || die "passwordless sudo is required"
[[ -x /usr/bin/qemu-aarch64-static ]] \
    || die "qemu-user-static is required at /usr/bin/qemu-aarch64-static"

mkdir -p "$work_dir"
available_kib="$(df --output=avail "$work_dir" | tail -1 | tr -d ' ')"
(( available_kib >= 200 * 1024 * 1024 )) \
    || die "at least 200 GiB of free build disk is required"

work_dir="$(realpath -m "$work_dir")"
output_dir="$(realpath -m "$output_dir")"
[[ "$work_dir" != "/" && "$output_dir" != "/" ]] \
    || die "work and output directories must not be the filesystem root"
[[ ! -e "$output_dir" ]] \
    || die "output directory already exists: $output_dir"
downloads="$work_dir/downloads"
publish_dir="$work_dir/publish"
manager_publish_dir="$work_dir/manager-publish"
installer_publish_dir="$work_dir/installer-publish"
redis_dir="$work_dir/redis"
telemetry_dir="$work_dir/telemetry"
voice_dir="$work_dir/voice"
bundle_root="$work_dir/bundle"
bsp_dir="$work_dir/bsp"
sd_bsp_dir="$work_dir/sd-bsp"
raw_dir="$output_dir/raw"
mkdir -p "$downloads" "$raw_dir"

download_sha1() {
    local url="$1"
    local expected="$2"
    local destination="$3"

    if [[ ! -f "$destination" ]]; then
        curl --fail --location --retry 3 --output "$destination" "$url"
    fi
    printf '%s  %s\n' "$expected" "$destination" | sha1sum --check --status \
        || die "download checksum failed: $destination"
}

download_sha256() {
    local url="$1"
    local expected="$2"
    local destination="$3"

    if [[ ! -f "$destination" ]]; then
        curl --fail --location --retry 3 --output "$destination" "$url"
    fi
    printf '%s  %s\n' "$expected" "$destination" | sha256sum --check --status \
        || die "download checksum failed: $destination"
}

download_sha1 \
    "$JETSON_BSP_URL" \
    "$JETSON_BSP_SHA1" \
    "$downloads/Jetson_Linux_R${JETSON_LINUX_VERSION}_aarch64.tbz2"
download_sha1 \
    "$JETSON_ROOTFS_URL" \
    "$JETSON_ROOTFS_SHA1" \
    "$downloads/Tegra_Linux_Sample-Root-Filesystem_R${JETSON_LINUX_VERSION}_aarch64.tbz2"
download_sha256 \
    "$OTELCOL_URL" \
    "$OTELCOL_SHA256" \
    "$downloads/otelcol-contrib_${OTELCOL_VERSION}_linux_arm64.tar.gz"
download_sha256 \
    "$REDIS_EXPORTER_URL" \
    "$REDIS_EXPORTER_SHA256" \
    "$downloads/redis_exporter-v${REDIS_EXPORTER_VERSION}.linux-arm64.tar.gz"

rm -rf \
    "$publish_dir" \
    "$manager_publish_dir" \
    "$installer_publish_dir" \
    "$redis_dir" \
    "$telemetry_dir" \
    "$voice_dir" \
    "$bundle_root"
dotnet publish "$repo_root/lucia.AgentHost/lucia.AgentHost.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    -p:UseAppHost=true \
    -p:PublishTrimmed=false \
    -p:PublishReadyToRun=true \
    -p:CopilotSkipCliDownload=true \
    --output "$publish_dir"
dotnet publish "$repo_root/lucia.ApplianceManager/lucia.ApplianceManager.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output "$manager_publish_dir"
npm --prefix "$repo_root/lucia-dashboard" ci
npm --prefix "$repo_root/lucia-dashboard" run build
dotnet publish "$repo_root/lucia.InstallerHost/lucia.InstallerHost.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    --output "$installer_publish_dir"
mkdir -p "$installer_publish_dir/wwwroot"
cp -a "$repo_root/lucia-dashboard/dist/." "$installer_publish_dir/wwwroot/"

mkdir -p "$redis_dir"
docker run --rm --platform linux/arm64 \
    -v "$redis_dir:/output" \
    ubuntu:22.04 \
    bash -lc "
        set -euo pipefail
        export DEBIAN_FRONTEND=noninteractive
        apt-get update -qq
        apt-get install -y -qq ca-certificates git build-essential >/dev/null
        git clone --quiet --depth 1 --branch '$REDIS_VERSION' \
            https://github.com/redis/redis.git /src/redis
        test \"\$(git -C /src/redis rev-parse HEAD)\" = '$REDIS_COMMIT'
        make -C /src/redis -j2 BUILD_TLS=no MALLOC=libc redis-server >/dev/null
        install -m 0755 /src/redis/src/redis-server /output/redis-server
    "

mkdir -p "$telemetry_dir/otelcol" "$telemetry_dir/redis-exporter"
tar -xzf \
    "$downloads/otelcol-contrib_${OTELCOL_VERSION}_linux_arm64.tar.gz" \
    -C "$telemetry_dir/otelcol"
tar -xzf \
    "$downloads/redis_exporter-v${REDIS_EXPORTER_VERSION}.linux-arm64.tar.gz" \
    --strip-components=1 \
    -C "$telemetry_dir/redis-exporter"
[[ -x "$telemetry_dir/otelcol/otelcol-contrib" ]] \
    || die "Collector archive did not contain otelcol-contrib"
[[ -x "$telemetry_dir/redis-exporter/redis_exporter" ]] \
    || die "Redis exporter archive did not contain redis_exporter"

voice_image="lucia-appliance-voice-assets:${version}"
docker buildx build \
    --platform linux/arm64 \
    --load \
    --tag "$voice_image" \
    --file "$repo_root/infra/docker/Dockerfile.agenthost-jetson-voice" \
    "$repo_root"
voice_container="$(docker create "$voice_image")"
trap 'docker rm -f "$voice_container" >/dev/null 2>&1 || true' EXIT
mkdir -p "$voice_dir"
docker cp "$voice_container:/app/runtimes/linux-arm64/native" "$voice_dir/native"
docker cp "$voice_container:/app/models" "$voice_dir/models"
docker cp "$voice_container:/app/plugins" "$voice_dir/plugins"
docker rm "$voice_container" >/dev/null
trap - EXIT

sudo "$repo_root/infra/appliance/build-native-bundle.sh" \
    --version "$version" \
    --publish-dir "$publish_dir" \
    --manager-dir "$manager_publish_dir" \
    --dashboard-dir "$repo_root/lucia-dashboard/dist" \
    --redis-server "$redis_dir/redis-server" \
    --native-dir "$voice_dir/native" \
    --models-dir "$voice_dir/models" \
    --plugins-dir "$voice_dir/plugins" \
    --otelcol "$telemetry_dir/otelcol/otelcol-contrib" \
    --redis-exporter "$telemetry_dir/redis-exporter/redis_exporter" \
    --output-dir "$bundle_root"
sudo chown -R "$(id -u):$(id -g)" "$bundle_root"
tar -I 'zstd -T0 -10' \
    -cf "$raw_dir/lucia-appliance-${version}-lucia.tar.zst" \
    -C "$bundle_root" \
    .

prepare_bsp() {
    local destination="$1"

    if findmnt -rn -o TARGET \
        | awk -v root="$destination" \
            '$0 == root || index($0, root "/") == 1 { found = 1 } END { exit !found }'; then
        die "refusing to replace BSP directory with active mounts: $destination"
    fi
    sudo rm -rf --one-file-system -- "$destination"
    sudo mkdir -p "$destination"
    sudo tar -xpf \
        "$downloads/Jetson_Linux_R${JETSON_LINUX_VERSION}_aarch64.tbz2" \
        -C "$destination"
    sudo tar -xpf \
        "$downloads/Tegra_Linux_Sample-Root-Filesystem_R${JETSON_LINUX_VERSION}_aarch64.tbz2" \
        -C "$destination/Linux_for_Tegra/rootfs"
    (
        cd "$destination/Linux_for_Tegra"
        sudo ./tools/l4t_flash_prerequisites.sh
        sudo ./apply_binaries.sh
    )
}

install_compute_runtime() (
    local root="$1"

    cleanup_chroot() {
        local cleanup_failed=0
        local target

        for target in "$root/dev/pts" "$root/dev" "$root/proc" "$root/sys"; do
            if mountpoint -q "$target" && ! sudo umount "$target"; then
                cleanup_failed=1
            fi
        done
        sudo rm -f "$root/usr/bin/qemu-aarch64-static"
        if findmnt -rn -o TARGET \
            | awk -v root="$root" \
                '$0 == root || index($0, root "/") == 1 { found = 1 } END { exit !found }'; then
            printf 'Error: chroot cleanup left active mounts under %s\n' "$root" >&2
            cleanup_failed=1
        fi
        return "$cleanup_failed"
    }
    on_exit() {
        local status=$?

        trap - EXIT
        cleanup_chroot || status=1
        exit "$status"
    }
    trap on_exit EXIT

    sudo cp /usr/bin/qemu-aarch64-static "$root/usr/bin/"
    sudo cp /etc/resolv.conf "$root/etc/resolv.conf"
    sudo sed -i \
        's#jetson/<SOC>#jetson/t234#' \
        "$root/etc/apt/sources.list.d/nvidia-l4t-apt-source.list"
    sudo mount --bind /dev "$root/dev"
    sudo mount --bind /dev/pts "$root/dev/pts"
    sudo mount -t proc proc "$root/proc"
    sudo mount -t sysfs sys "$root/sys"
    sudo chroot "$root" apt-get update -qq
    sudo chroot "$root" apt-get install -y -qq --no-install-recommends \
        cuda-cudart-12-6=12.6.68-1 \
        cuda-nvrtc-12-6=12.6.68-1 \
        libcublas-12-6=12.6.1.4-1 \
        libcufft-12-6=11.2.6.59-1 \
        libcudnn9-cuda-12=9.3.0.75-1
    sudo chroot "$root" apt-get clean
    sudo rm -rf "$root/var/lib/apt/lists"/*
)

prepare_bsp "$bsp_dir"
root="$bsp_dir/Linux_for_Tegra/rootfs"
install_compute_runtime "$root"
sudo cp -a "$repo_root/infra/appliance/rootfs/." "$root/"
sudo mkdir -p "$root/opt/lucia" "$root/var/lib/lucia"
grep -q '^PARTLABEL=LUCIA ' "$root/etc/fstab" \
    || printf 'PARTLABEL=LUCIA /opt/lucia ext4 defaults,nodev,nosuid 0 2\n' \
        | sudo tee -a "$root/etc/fstab" >/dev/null
grep -q '^PARTLABEL=LUCIA_DATA ' "$root/etc/fstab" \
    || printf 'PARTLABEL=LUCIA_DATA /var/lib/lucia ext4 defaults,nodev,nosuid 0 2\n' \
        | sudo tee -a "$root/etc/fstab" >/dev/null
sudo systemctl --root="$root" enable \
    lucia-appliance-manager.service \
    lucia-redis.service \
    lucia-agenthost.service
sudo cp /usr/bin/qemu-aarch64-static "$root/usr/bin/"
recovery_password="$(openssl rand -base64 24)"
sudo "$bsp_dir/Linux_for_Tegra/tools/l4t_create_default_user.sh" \
    --username lucia-recovery \
    --password "$recovery_password" \
    --hostname lucia \
    --accept-license >/dev/null
unset recovery_password
sudo rm -f "$root/usr/bin/qemu-aarch64-static"
sudo sed -i -E \
    's#^(lucia-recovery:)[^:]*:#\1!:#' \
    "$root/etc/shadow"
sudo truncate --size 0 "$root/etc/machine-id"
sudo rm -f "$root/var/lib/dbus/machine-id"

(
    cd "$bsp_dir/Linux_for_Tegra"
    sudo env \
        USER=root \
        BOARDID=3767 \
        BOARDSKU=0005 \
        FAB=300 \
        CHIP_SKU=00:00:00:D5 \
        FUSELEVEL=fuselevel_production \
        ROOTFS_AB=1 \
        ./tools/kernel_flash/l4t_initrd_flash_internal.sh \
            --no-flash \
            --external-device nvme0n1 \
            -S 16GiB \
            -c ./tools/kernel_flash/flash_l4t_nvme_rootfs_ab.xml \
            --network usb0 \
            jetson-orin-nano-devkit-super \
            external
)

sudo "$repo_root/infra/appliance/installer/build-loop-image.sh" \
    "$bsp_dir/Linux_for_Tegra" \
    "$work_dir/lucia-nvme-${version}.img" \
    "$MINIMUM_DISK_BYTES"
sudo "$repo_root/infra/appliance/installer/finalize-loop-image.sh" \
    "$work_dir/lucia-nvme-${version}.img" \
    6G \
    "$bundle_root"
sudo zstd -T0 -10 --long=27 --force \
    "$work_dir/lucia-nvme-${version}.img" \
    -o "$work_dir/lucia-nvme-${version}.img.zst"

external_images="$bsp_dir/Linux_for_Tegra/tools/kernel_flash/images/external"
tar -I 'zstd -T0 -10' \
    -cf "$raw_dir/lucia-appliance-${version}-os.tar.zst" \
    -C "$external_images" \
    system.img \
    system.img_b \
    boot.img \
    boot.img_b \
    kernel_tegra234-p3768-0000+p3767-0005-nv-super.dtb

prepare_bsp "$sd_bsp_dir"
sd_root="$sd_bsp_dir/Linux_for_Tegra/rootfs"
sudo cp -a "$repo_root/infra/appliance/installer/rootfs/." "$sd_root/"
sudo install -d \
    "$sd_root/etc/lucia-installer" \
    "$sd_root/opt/lucia-installer/app" \
    "$sd_root/var/lib/lucia-installer"
sudo install -m 0755 \
    "$repo_root/infra/appliance/installer/lucia-install" \
    "$sd_root/usr/libexec/lucia/lucia-install"
sudo cp -a "$installer_publish_dir/." "$sd_root/opt/lucia-installer/app/"
sudo cp \
    "$sd_root/etc/lucia-installer/installer.env.example" \
    "$sd_root/etc/lucia-installer/installer.env"
setup_suffix="$(openssl rand -hex 3 | tr '[:lower:]' '[:upper:]')"
printf 'LUCIA_SETUP_SSID=Lucia-%s\n' "$setup_suffix" \
    | sudo tee "$sd_root/etc/lucia-installer/bootstrap.env" >/dev/null
sudo chmod 0600 \
    "$sd_root/etc/lucia-installer/bootstrap.env" \
    "$sd_root/etc/lucia-installer/installer.env"
sudo install -m 0644 \
    "$work_dir/lucia-nvme-${version}.img.zst" \
    "$sd_root/opt/lucia-installer/lucia-nvme.img.zst"
nvme_sha256="$(sha256sum "$work_dir/lucia-nvme-${version}.img.zst" | cut -d' ' -f1)"
printf '%s  lucia-nvme.img.zst\n' "$nvme_sha256" \
    | sudo tee "$sd_root/opt/lucia-installer/lucia-nvme.img.zst.sha256" \
        >/dev/null
sudo systemctl --root="$sd_root" enable \
    lucia-network-bootstrap.service \
    lucia-installer-host.service \
    lucia-firstboot-install.service
sudo cp /usr/bin/qemu-aarch64-static "$sd_root/usr/bin/"
recovery_password="$(openssl rand -base64 24)"
sudo "$sd_bsp_dir/Linux_for_Tegra/tools/l4t_create_default_user.sh" \
    --username lucia-recovery \
    --password "$recovery_password" \
    --hostname lucia \
    --accept-license >/dev/null
unset recovery_password
sudo rm -f "$sd_root/usr/bin/qemu-aarch64-static"
sudo sed -i -E \
    's#^(lucia-recovery:)[^:]*:#\1!:#' \
    "$sd_root/etc/shadow"
sudo truncate --size 0 "$sd_root/etc/machine-id"
sudo rm -f "$sd_root/var/lib/dbus/machine-id"

sudo "$repo_root/infra/appliance/installer/build-sd-image.sh" \
    "$sd_bsp_dir/Linux_for_Tegra" \
    "$work_dir/lucia-installer-sd-${version}.img"
sudo zstd -T0 -10 --long=27 --force \
    "$work_dir/lucia-installer-sd-${version}.img" \
    -o "$raw_dir/lucia-appliance-${version}-installer.img.zst"

printf 'installer=%s\n' "$raw_dir/lucia-appliance-${version}-installer.img.zst"
printf 'lucia=%s\n' "$raw_dir/lucia-appliance-${version}-lucia.tar.zst"
printf 'os=%s\n' "$raw_dir/lucia-appliance-${version}-os.tar.zst"
