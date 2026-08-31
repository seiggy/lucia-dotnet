#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/appliance.lock"

[[ "${#COMPUTE_PACKAGES[@]}" -eq 8 ]]
[[ "$JETSON_BSP_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$JETSON_ROOTFS_SHA256" =~ ^[0-9a-f]{64}$ ]]
! grep -q 'SHA1\|download_sha1\|sha1sum' \
    "$script_dir/build-release-assets.sh" \
    "$script_dir/appliance.lock"
for package in "${COMPUTE_PACKAGES[@]}"; do
    read -r sha256 url <<< "$package"
    [[ "$sha256" =~ ^[0-9a-f]{64}$ ]]
    [[ "$url" == https://repo.download.nvidia.com/jetson/common/pool/*.deb ]]
done

grep -q 'sudo chroot "$root" dpkg --install' \
    "$script_dir/build-release-assets.sh"
! grep -Eq 'apt-get (update|install)' \
    "$script_dir/build-release-assets.sh"
grep -q 'gpasswd --delete lucia-recovery sudo' \
    "$script_dir/build-release-assets.sh"
grep -q 'usermod --shell.*lucia-recovery' \
    "$script_dir/build-release-assets.sh"

echo "PASS: release rootfs uses pinned packages and restricted recovery"
